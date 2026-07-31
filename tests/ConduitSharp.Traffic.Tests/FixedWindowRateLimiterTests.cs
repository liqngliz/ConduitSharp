using Xunit;
using ConduitSharp.Traffic.RateLimiting;

namespace ConduitSharp.Traffic.Tests;

public sealed class FixedWindowRateLimiterTests
{
    private const int Window = 60;

    [Fact]
    public void TryAcquire_WithinLimit_ReturnsTrue()
    {
        var limiter = new FixedWindowRateLimiter();

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("client-a", Window, maxRequests: 5).Allowed, $"request {i + 1} should be allowed");
    }

    [Fact]
    public void TryAcquire_OverLimit_ReturnsFalse()
    {
        var limiter = new FixedWindowRateLimiter();

        limiter.TryAcquire("c", Window, 2);
        limiter.TryAcquire("c", Window, 2);

        Assert.False(limiter.TryAcquire("c", Window, 2).Allowed);
    }

    [Fact]
    public void TryAcquire_MaxRequestsOne_SecondCallReturnsFalse()
    {
        var limiter = new FixedWindowRateLimiter();

        Assert.True(limiter.TryAcquire("k", Window, 1).Allowed);
        Assert.False(limiter.TryAcquire("k", Window, 1).Allowed);
    }

    [Fact]
    public void TryAcquire_DifferentKeys_HaveIndependentCounters()
    {
        var limiter = new FixedWindowRateLimiter();

        Assert.True(limiter.TryAcquire("client-a", Window, 1).Allowed);
        Assert.True(limiter.TryAcquire("client-b", Window, 1).Allowed);
    }

    [Fact]
    public void TryAcquire_ZeroMaxRequests_AlwaysReturnsFalse()
    {
        var limiter = new FixedWindowRateLimiter();

        Assert.False(limiter.TryAcquire("any", Window, 0).Allowed);
    }

    [Fact]
    public void Denied_RetryAfter_CountsToTheWindowRollover_NotTheFullWindow()
    {
        long now = 999_980;
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        Assert.True(limiter.TryAcquire("k", Window, 1).Allowed);
        var denied = limiter.TryAcquire("k", Window, 1);

        Assert.False(denied.Allowed);
        Assert.Equal(40, denied.RetryAfterSeconds);
    }

    [Fact]
    public void Denied_RetryAfter_IsNeverZero_AtTheInstantOfRollover()
    {
        long now = 1_000_019;
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        Assert.True(limiter.TryAcquire("k", Window, 1).Allowed);
        var denied = limiter.TryAcquire("k", Window, 1);

        Assert.False(denied.Allowed);
        Assert.Equal(1, denied.RetryAfterSeconds);
    }

    [Fact]
    public void Allowed_ReportsNoRetryAfter()
    {
        var limiter = new FixedWindowRateLimiter();
        Assert.Equal(new RateLimitDecision(true, 0), limiter.TryAcquire("k", Window, 1));
    }

    [Fact]
    public void TryAcquire_ExpiredWindowEntry_IsEvicted()
    {
        long now = 60;
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        limiter.TryAcquire("client", Window, 100);
        Assert.Equal(1, limiter.CounterCount);

        now = 120;
        limiter.TryAcquire("client", Window, 100);

        Assert.Equal(1, limiter.CounterCount);
    }

    [Fact]
    public void TryAcquire_MultipleKeysExpiredWindow_AllEvicted()
    {
        long now = 60;
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        limiter.TryAcquire("a", Window, 100);
        limiter.TryAcquire("b", Window, 100);
        limiter.TryAcquire("c", Window, 100);
        Assert.Equal(3, limiter.CounterCount);

        now = 120;
        limiter.TryAcquire("a", Window, 100);

        Assert.Equal(1, limiter.CounterCount);
    }

    [Fact]
    public async Task TryAcquire_ConcurrentAcquiresDuringSweep_ExactQuota_ExpiredEvicted_LiveKept()
    {
        long now = 60;
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => Volatile.Read(ref now));

        for (var i = 0; i < 1_000; i++)
            limiter.TryAcquire($"stale-{i}", Window, 100);
        Assert.Equal(1_000, limiter.CounterCount);

        Volatile.Write(ref now, 120);

        long allowed = 0;
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
                if (limiter.TryAcquire("live", Window, maxRequests: 500).Allowed)
                    Interlocked.Increment(ref allowed);
        })));

        Assert.Equal(500, Interlocked.Read(ref allowed));
        Assert.Equal(1, limiter.CounterCount);
        Assert.False(limiter.TryAcquire("live", Window, 500).Allowed);
    }

    [Fact]
    public void SharedStore_ShortWindowAcquires_DoNotResetLongWindowCounters()
    {
        long now = 1_000_000;
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        Assert.True(limiter.TryAcquire("caller", windowSeconds: 60, maxRequests: 2).Allowed);
        Assert.True(limiter.TryAcquire("caller", windowSeconds: 60, maxRequests: 2).Allowed);

        limiter.TryAcquire("caller", windowSeconds: 1, maxRequests: 100);
        now += 1;
        limiter.TryAcquire("caller", windowSeconds: 1, maxRequests: 100);

        Assert.False(limiter.TryAcquire("caller", windowSeconds: 60, maxRequests: 2).Allowed);
    }

    [Fact]
    public void SharedStore_SameKeyDifferentWindows_CountersAreDistinctPerWindowLength()
    {
        long now = 1_000_000;
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        Assert.True(limiter.TryAcquire("k", windowSeconds: 60, maxRequests: 1).Allowed);
        Assert.True(limiter.TryAcquire("k", windowSeconds: 30, maxRequests: 1).Allowed);

        Assert.False(limiter.TryAcquire("k", windowSeconds: 60, maxRequests: 1).Allowed);
        Assert.False(limiter.TryAcquire("k", windowSeconds: 30, maxRequests: 1).Allowed);
    }
}

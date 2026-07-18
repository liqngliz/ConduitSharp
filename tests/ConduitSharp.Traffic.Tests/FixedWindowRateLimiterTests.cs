using Xunit;
using ConduitSharp.Traffic.RateLimiting;

namespace ConduitSharp.Traffic.Tests;

public sealed class FixedWindowRateLimiterTests
{
    private const int Window = 60;

    // -------------------------------------------------------------------------
    // Within limit
    // -------------------------------------------------------------------------

    [Fact]
    public void TryAcquire_WithinLimit_ReturnsTrue()
    {
        var limiter = new FixedWindowRateLimiter();

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("client-a", Window, maxRequests: 5).Allowed, $"request {i + 1} should be allowed");
    }

    // -------------------------------------------------------------------------
    // Over limit
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Different keys are independent
    // -------------------------------------------------------------------------

    [Fact]
    public void TryAcquire_DifferentKeys_HaveIndependentCounters()
    {
        var limiter = new FixedWindowRateLimiter();

        Assert.True(limiter.TryAcquire("client-a", Window, 1).Allowed);
        Assert.True(limiter.TryAcquire("client-b", Window, 1).Allowed); // independent counter — still allowed
    }

    // -------------------------------------------------------------------------
    // Zero limit
    // -------------------------------------------------------------------------

    [Fact]
    public void TryAcquire_ZeroMaxRequests_AlwaysReturnsFalse()
    {
        var limiter = new FixedWindowRateLimiter();

        Assert.False(limiter.TryAcquire("any", Window, 0).Allowed);
    }

    // -------------------------------------------------------------------------
    // Retry-After — the algorithm owns it, because only it knows when a permit frees
    // -------------------------------------------------------------------------

    [Fact]
    public void Denied_RetryAfter_CountsToTheWindowRollover_NotTheFullWindow()
    {
        // Windows are epoch-aligned, so 20 s into a 60 s window the answer is 40, not 60.
        long now = 999_980; // 999_980 % 60 == 20
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        Assert.True(limiter.TryAcquire("k", Window, 1).Allowed);
        var denied = limiter.TryAcquire("k", Window, 1);

        Assert.False(denied.Allowed);
        Assert.Equal(40, denied.RetryAfterSeconds);
    }

    [Fact]
    public void Denied_RetryAfter_IsNeverZero_AtTheInstantOfRollover()
    {
        // 59 s into the window: 1 s remains. A Retry-After of 0 would invite an instant retry.
        long now = 1_000_019; // 1_000_019 % 60 == 59
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

    // -------------------------------------------------------------------------
    // Eviction — expired window entries are removed to prevent memory leak
    // -------------------------------------------------------------------------

    [Fact]
    public void TryAcquire_ExpiredWindowEntry_IsEvicted()
    {
        long now = 60; // windowId = 1
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        limiter.TryAcquire("client", Window, 100);
        Assert.Equal(1, limiter.CounterCount);

        now = 120; // windowId = 2
        limiter.TryAcquire("client", Window, 100);

        // Old entry (windowId=1) should be evicted; only the current window slot remains.
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

        // All three old entries evicted; only "a" in the new window remains.
        Assert.Equal(1, limiter.CounterCount);
    }

    [Fact]
    public async Task TryAcquire_ConcurrentAcquiresDuringSweep_ExactQuota_ExpiredEvicted_LiveKept()
    {
        // The sweep is lock-free (CAS elects one sweeper, the rest proceed) and runs
        // while other threads GetOrAdd/Increment the same dictionary. Under contention:
        // the quota must stay exact (no lost or double counts), expired entries must go,
        // and live counters must survive the sweep.
        long now = 60; // windowId = 1
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => Volatile.Read(ref now));

        for (var i = 0; i < 1_000; i++)
            limiter.TryAcquire($"stale-{i}", Window, 100);
        Assert.Equal(1_000, limiter.CounterCount);

        // windowId = 2: every stale entry is expired (ExpiresAt=120) and the sweep is due.
        Volatile.Write(ref now, 120);

        long allowed = 0;
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
                if (limiter.TryAcquire("live", Window, maxRequests: 500).Allowed)
                    Interlocked.Increment(ref allowed);
        })));

        Assert.Equal(500, Interlocked.Read(ref allowed)); // exactly the quota across 3200 racing attempts
        Assert.Equal(1, limiter.CounterCount);            // 1000 expired gone, the live counter kept
        Assert.False(limiter.TryAcquire("live", Window, 500).Allowed); // and still enforcing
    }

    // -------------------------------------------------------------------------
    // Shared store, mixed window lengths — regression for cross-scale eviction
    // -------------------------------------------------------------------------

    [Fact]
    public void SharedStore_ShortWindowAcquires_DoNotResetLongWindowCounters()
    {
        // One store serving both a 1 s window and a 60 s window — now the single-singleton path:
        // one limiter, different windows arriving per call. The old eviction compared raw windowIds
        // across entries: a 1 s windowId (~epoch seconds) always exceeded a 60 s windowId
        // (~epoch/60), so every short-window acquire wiped the long window's live counters and its
        // limit was never enforced.
        long now = 1_000_000;
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        Assert.True(limiter.TryAcquire("caller", windowSeconds: 60, maxRequests: 2).Allowed);
        Assert.True(limiter.TryAcquire("caller", windowSeconds: 60, maxRequests: 2).Allowed);

        limiter.TryAcquire("caller", windowSeconds: 1, maxRequests: 100); // must not evict the 60 s counter
        now += 1;                                                         // short window rolls over; long window has not
        limiter.TryAcquire("caller", windowSeconds: 1, maxRequests: 100);

        Assert.False(limiter.TryAcquire("caller", windowSeconds: 60, maxRequests: 2).Allowed); // still over 2-per-60s
    }

    [Fact]
    public void SharedStore_SameKeyDifferentWindows_CountersAreDistinctPerWindowLength()
    {
        // Same caller key under two window lengths on one shared store — each must track its own
        // count (their windowIds differ in scale).
        long now = 1_000_000;
        var limiter = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now);

        Assert.True(limiter.TryAcquire("k", windowSeconds: 60, maxRequests: 1).Allowed);
        Assert.True(limiter.TryAcquire("k", windowSeconds: 30, maxRequests: 1).Allowed); // own scale — own counter

        Assert.False(limiter.TryAcquire("k", windowSeconds: 60, maxRequests: 1).Allowed);
        Assert.False(limiter.TryAcquire("k", windowSeconds: 30, maxRequests: 1).Allowed);
    }
}

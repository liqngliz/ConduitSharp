using Xunit;
using ConduitSharp.Traffic.RateLimiting;

namespace ConduitSharp.RateLimit.SlidingWindow.Tests;

public sealed class SlidingWindowRateLimiterTests
{
    private static (SlidingWindowRateLimiter limiter, Action<long> advance) At(long startMillis)
    {
        var now = startMillis;
        return (new SlidingWindowRateLimiter(() => now), ms => now += ms);
    }

    [Fact]
    public void UnderQuota_Allows()
    {
        var (limiter, _) = At(0);
        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("k", windowSeconds: 60, maxRequests: 5).Allowed);
    }

    [Fact]
    public void OverQuota_Denies()
    {
        var (limiter, _) = At(0);
        limiter.TryAcquire("k", 60, 2);
        limiter.TryAcquire("k", 60, 2);
        Assert.False(limiter.TryAcquire("k", 60, 2).Allowed);
    }

    [Fact]
    public void KeysAreIndependent()
    {
        var (limiter, _) = At(0);
        Assert.True(limiter.TryAcquire("a", 60, 1).Allowed);
        Assert.False(limiter.TryAcquire("a", 60, 1).Allowed);
        Assert.True(limiter.TryAcquire("b", 60, 1).Allowed);
    }

    [Fact]
    public void PermitFreesExactlyWhenTheOldestRequestAgesOut()
    {
        // The sliding property: the window travels with the request, it does not reset on a
        // boundary. A permit taken at t=0 is returned at t=60s and not one millisecond sooner.
        var (limiter, advance) = At(1_000_000);
        Assert.True(limiter.TryAcquire("k", 60, 1).Allowed);

        advance(59_999);
        Assert.False(limiter.TryAcquire("k", 60, 1).Allowed);

        advance(1); // the original permit is now exactly 60s old
        Assert.True(limiter.TryAcquire("k", 60, 1).Allowed);
    }

    [Fact]
    public void RetryAfter_CountsToTheOldestRequestAgingOut_NotToAWindowBoundary()
    {
        // The reason RateLimitDecision carries a retry hint at all: this answer is knowable only
        // to the algorithm. A fixed window would have said "time until the next aligned boundary",
        // which for a sliding log is meaningless.
        var (limiter, advance) = At(1_000_000);
        Assert.True(limiter.TryAcquire("k", 60, 1).Allowed);

        advance(20_000); // 20s in: the permit frees 40s from now
        var denied = limiter.TryAcquire("k", 60, 1);

        Assert.False(denied.Allowed);
        Assert.Equal(40, denied.RetryAfterSeconds);
    }

    [Fact]
    public void RetryAfter_IsNeverZero_WhenDenied()
    {
        // A Retry-After of 0 invites an instant retry storm; the contract floors it at 1.
        var (limiter, advance) = At(1_000_000);
        Assert.True(limiter.TryAcquire("k", 60, 1).Allowed);

        advance(59_999); // frees in 1ms — must still round up to a whole second
        var denied = limiter.TryAcquire("k", 60, 1);

        Assert.False(denied.Allowed);
        Assert.Equal(1, denied.RetryAfterSeconds);
    }

    [Fact]
    public void Allowed_ReportsNoRetryAfter()
    {
        var (limiter, _) = At(0);
        Assert.Equal(new RateLimitDecision(true, 0), limiter.TryAcquire("k", 60, 1));
    }

    [Fact]
    public void BoundarySpanningBurst_IsRefused_WhereAFixedWindowWouldAllowIt()
    {
        // The entire reason this algorithm exists, asserted against the one it replaces.
        // Two full quotas either side of an aligned boundary = 2x the nominal rate in one instant.
        // Both limiters see identical calls; only the fixed window lets the burst through.
        const int window = 60, quota = 5;
        var now = 59_000L; // 59s into an aligned 60s window
        var sliding = new SlidingWindowRateLimiter(() => now);
        var fixedWindow = new FixedWindowRateLimiter(new InMemoryRateLimitStore(), () => now / 1000);

        for (var i = 0; i < quota; i++)
        {
            Assert.True(sliding.TryAcquire("k", window, quota).Allowed);
            Assert.True(fixedWindow.TryAcquire("k", window, quota).Allowed);
        }

        now += 2_000; // 61s — past the boundary, so the fixed window's counter resets

        var fixedAllowsBurst = fixedWindow.TryAcquire("k", window, quota).Allowed;
        var slidingAllowsBurst = sliding.TryAcquire("k", window, quota).Allowed;

        Assert.True(fixedAllowsBurst, "fixed window resets on the boundary — 10 requests in 3s");
        Assert.False(slidingAllowsBurst, "sliding log still sees 5 requests within the last 60s");
    }

    [Fact]
    public void ExpiredTimestamps_AreDropped_SoMemoryStaysBounded()
    {
        // Timestamps are swept on use; a key never exceeds maxRequests entries even under
        // sustained load across many windows.
        var (limiter, advance) = At(1_000_000);
        for (var i = 0; i < 50; i++)
        {
            limiter.TryAcquire("k", 1, 2);
            advance(500);
        }
        Assert.Equal(1, limiter.TrackedKeys);
        Assert.True(limiter.TryAcquire("k", 1, 2).Allowed);
    }

    [Fact]
    public async Task ConcurrentAcquires_NeverExceedTheQuota()
    {
        var limiter = new SlidingWindowRateLimiter();
        const int quota = 50;
        var allowed = 0;
        await Task.WhenAll(Enumerable.Range(0, Environment.ProcessorCount * 40).Select(_ => Task.Run(() =>
        {
            if (limiter.TryAcquire("hot", 60, quota).Allowed)
                Interlocked.Increment(ref allowed);
        })));
        Assert.Equal(quota, allowed);
    }
}

namespace ConduitSharp.Traffic.RateLimiting;

/// <summary>
/// Fixed-window rate limiter, the default algorithm. Each key gets <c>maxRequests</c> permits per
/// <c>windowSeconds</c>-second window, aligned to the Unix epoch, counted in an
/// <see cref="IRateLimitStore"/> so a distributed store shares the quota across replicas.
///
/// <para>Stateless and thread-safe: window and quota arrive per call and all mutable state lives
/// in the store.</para>
/// </summary>
public sealed class FixedWindowRateLimiter : IRateLimiter
{
    private readonly IRateLimitStore _store;
    private readonly Func<long> _nowSeconds;

    /// <summary>Counts in-process — quotas are per gateway instance.</summary>
    public FixedWindowRateLimiter() : this(new InMemoryRateLimitStore()) { }

    /// <summary>Counts in <paramref name="store"/>; a distributed store shares quotas across replicas.</summary>
    public FixedWindowRateLimiter(IRateLimitStore store)
        : this(store, () => DateTimeOffset.UtcNow.ToUnixTimeSeconds()) { }

    internal FixedWindowRateLimiter(IRateLimitStore store, Func<long> clock)
    {
        _store = store;
        _nowSeconds = clock;
    }

    /// <inheritdoc />
    public RateLimitDecision TryAcquire(string key, int windowSeconds, long maxRequests)
    {
        var now = _nowSeconds();
        var windowId = now / windowSeconds;
        if (_store.TryAcquire(key, windowId, windowSeconds, maxRequests))
            return RateLimitDecision.Allow;

        return RateLimitDecision.Deny(windowSeconds - now % windowSeconds);
    }

    /// <summary>Exposed for testing — returns the number of tracked counter slots.</summary>
    internal int CounterCount => _store is InMemoryRateLimitStore inmemory ? inmemory.CounterCount : 0;
}

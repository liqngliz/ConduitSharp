namespace ConduitSharp.Traffic.RateLimiting;

/// <summary>
/// Fixed-window rate limiter — the default algorithm.
///
/// Each unique key gets <c>maxRequests</c> permits per <c>windowSeconds</c>-second window, with
/// boundaries aligned to the Unix epoch (a 60 s window resets on the minute). Counting is
/// delegated to an <see cref="IRateLimitStore"/>, so swapping in a distributed store (Redis)
/// shares the quota across replicas without touching this class.
///
/// The trade: because windows are hard boundaries, a caller can spend a full quota at the end of
/// one window and another at the start of the next — up to 2x the nominal rate across that seam.
/// That is the price of O(1) state per key. The SlidingWindow example trades memory for a limit
/// that holds across every instant.
///
/// Stateless and thread-safe: window and quota arrive per call, and all mutable state lives in
/// the store.
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

        // Seconds until this window rolls over — not the full window length. Boundaries are
        // epoch-aligned, so the answer comes from the clock alone.
        return RateLimitDecision.Deny(windowSeconds - now % windowSeconds);
    }

    /// <summary>Exposed for testing — returns the number of tracked counter slots.</summary>
    internal int CounterCount => _store is InMemoryRateLimitStore inmemory ? inmemory.CounterCount : 0;
}

using System.Collections.Concurrent;

namespace ConduitSharp.Traffic.RateLimiting;

public interface IRateLimitStore
{
    bool TryAcquire(string key, long windowId, int windowSeconds, long maxRequests);
}

public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    // Sweeping at most this often bounds stale-entry memory without paying a full
    // table scan on every acquire (the hot path with per-caller keys is O(1) now).
    private const long SweepIntervalSeconds = 30;

    private sealed class Counter { public long Value; public long ExpiresAtSeconds; }

    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private long _nextSweepAtSeconds;

    public bool TryAcquire(string key, long windowId, int windowSeconds, long maxRequests)
    {
        // Epoch seconds at the start of the caller's current window — close enough to
        // "now" for sweep scheduling, and derived without the store needing its own clock.
        var nowSeconds = windowId * (long)windowSeconds;
        SweepIfDue(nowSeconds);

        var slotKey = $"{key}\0{windowId}";
        var counter = _counters.GetOrAdd(slotKey, _ => new Counter
        {
            // Each entry records its own absolute expiry: windows of different lengths
            // coexist in this shared store, so comparing raw windowIds across entries
            // (as the old per-acquire eviction did) would let a short-window route
            // evict a long-window route's still-live counters.
            ExpiresAtSeconds = (windowId + 1) * (long)windowSeconds,
        });
        return Interlocked.Increment(ref counter.Value) <= maxRequests;
    }

    internal int CounterCount => _counters.Count;

    private void SweepIfDue(long nowSeconds)
    {
        var due = Volatile.Read(ref _nextSweepAtSeconds);
        if (nowSeconds < due) return;
        // One thread wins the sweep; the rest skip it and proceed with their acquire.
        if (Interlocked.CompareExchange(ref _nextSweepAtSeconds, nowSeconds + SweepIntervalSeconds, due) != due)
            return;

        foreach (var (slotKey, counter) in _counters)
            if (counter.ExpiresAtSeconds <= nowSeconds)
                _counters.TryRemove(slotKey, out _);
    }
}

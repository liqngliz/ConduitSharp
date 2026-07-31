using System.Collections.Concurrent;

namespace ConduitSharp.Traffic.RateLimiting;

public interface IRateLimitStore
{
    bool TryAcquire(string key, long windowId, int windowSeconds, long maxRequests);

    /// <summary>
    /// Adds <paramref name="amount"/> to the counter for this window and returns the new total. Used
    /// by weighted limiters (e.g. token metering) that charge a variable cost after a request rather
    /// than one permit before it. On the first write of a window the entry gets a <c>windowSeconds</c>
    /// TTL. A failing distributed backend adds nothing and returns 0 (fail-open), same contract as
    /// <see cref="TryAcquire"/>.
    /// </summary>
    long Add(string key, long windowId, int windowSeconds, long amount);

    /// <summary>Current counter total for this window, or 0 if absent, expired, or the backend failed
    /// (fail-open). A read only — does not create or increment.</summary>
    long Peek(string key, long windowId);
}

public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private const long SweepIntervalSeconds = 30;

    private sealed class Counter { public long Value; public long ExpiresAtSeconds; }

    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private long _nextSweepAtSeconds;

    public bool TryAcquire(string key, long windowId, int windowSeconds, long maxRequests)
    {
        var nowSeconds = windowId * (long)windowSeconds;
        SweepIfDue(nowSeconds);

        var slotKey = $"{key}\0{windowId}";
        var counter = _counters.GetOrAdd(slotKey, _ => new Counter
        {
            ExpiresAtSeconds = (windowId + 1) * (long)windowSeconds,
        });
        return Interlocked.Increment(ref counter.Value) <= maxRequests;
    }

    public long Add(string key, long windowId, int windowSeconds, long amount)
    {
        var nowSeconds = windowId * (long)windowSeconds;
        SweepIfDue(nowSeconds);

        var slotKey = $"{key}\0{windowId}";
        var counter = _counters.GetOrAdd(slotKey, _ => new Counter
        {
            ExpiresAtSeconds = (windowId + 1) * (long)windowSeconds,
        });
        return Interlocked.Add(ref counter.Value, amount);
    }

    public long Peek(string key, long windowId)
    {
        var slotKey = $"{key}\0{windowId}";
        return _counters.TryGetValue(slotKey, out var counter) ? Interlocked.Read(ref counter.Value) : 0;
    }

    internal int CounterCount => _counters.Count;

    private void SweepIfDue(long nowSeconds)
    {
        var due = Volatile.Read(ref _nextSweepAtSeconds);
        if (nowSeconds < due) return;
        if (Interlocked.CompareExchange(ref _nextSweepAtSeconds, nowSeconds + SweepIntervalSeconds, due) != due)
            return;

        foreach (var (slotKey, counter) in _counters)
            if (counter.ExpiresAtSeconds <= nowSeconds)
                _counters.TryRemove(slotKey, out _);
    }
}

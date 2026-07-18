using System.Collections.Concurrent;

namespace ConduitSharp.Traffic.Caching;

/// <summary>
/// In-process cache backed by a <see cref="ConcurrentDictionary"/>.
/// Expired entries are evicted lazily on read (no background sweep), and the total
/// bytes held is capped: cache keys and vary headers are attacker-controlled, so an
/// unbounded cache is a trivially fillable heap. When a write would exceed the cap,
/// expired entries are evicted first, then live entries closest to expiry.
/// </summary>
public sealed class InMemoryCacheService : ICacheService
{
    /// <summary>Default total-bytes cap: 64 MiB.</summary>
    public const long DefaultMaxTotalBytes = 64 * 1024 * 1024;

    private readonly ConcurrentDictionary<string, (CachedResponse Response, DateTimeOffset Expiry, long Size)> _store = new();
    private readonly long _maxTotalBytes;
    private long _totalBytes;

    /// <param name="maxTotalBytes">
    /// Maximum approximate bytes of cached responses held in memory.
    /// Zero or negative disables the cap.
    /// </param>
    public InMemoryCacheService(long maxTotalBytes = DefaultMaxTotalBytes) =>
        _maxTotalBytes = maxTotalBytes;

    public ValueTask<CachedResponse?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var entry) && entry.Expiry > DateTimeOffset.UtcNow)
            return new ValueTask<CachedResponse?>(entry.Response);

        Remove(key);
        return new ValueTask<CachedResponse?>((CachedResponse?)null);
    }

    public Task SetAsync(string key, CachedResponse response, TimeSpan ttl, CancellationToken ct = default)
    {
        var size = EstimateSize(key, response);
        if (_maxTotalBytes > 0 && size > _maxTotalBytes)
            return Task.CompletedTask; // larger than the whole budget — don't cache

        Remove(key); // replace: release the old entry's bytes first
        if (_store.TryAdd(key, (response, DateTimeOffset.UtcNow.Add(ttl), size)))
            Interlocked.Add(ref _totalBytes, size);

        // ponytail: best-effort cap, not a hard invariant — concurrent SetAsync calls can each
        // pass this check before either evicts, briefly overshooting by up to one entry per
        // writer. Everyone converges on the next write. A hard cap needs a lock around
        // add+evict; take that only if the overshoot ever matters.
        if (_maxTotalBytes > 0 && Interlocked.Read(ref _totalBytes) > _maxTotalBytes)
            EvictUntilUnderBudget();

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public Task<int> RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default)
    {
        var removed = 0;
        foreach (var key in _store.Keys)
            if (key.StartsWith(keyPrefix, StringComparison.Ordinal) && Remove(key))
                removed++;
        return Task.FromResult(removed);
    }

    private bool Remove(string key)
    {
        if (!_store.TryRemove(key, out var entry))
            return false;
        Interlocked.Add(ref _totalBytes, -entry.Size);
        return true;
    }

    // ponytail: evicts by earliest expiry (no LRU access tracking); the O(n log n)
    // snapshot sort only runs when the budget is exceeded.
    private void EvictUntilUnderBudget()
    {
        foreach (var kv in _store.ToArray().OrderBy(kv => kv.Value.Expiry))
        {
            if (Interlocked.Read(ref _totalBytes) <= _maxTotalBytes)
                return;
            // KeyValuePair overload: only removes if the entry wasn't concurrently replaced,
            // so the size we subtract always matches the entry we removed.
            if (_store.TryRemove(kv))
                Interlocked.Add(ref _totalBytes, -kv.Value.Size);
        }
    }

    private static long EstimateSize(string key, CachedResponse response) =>
        sizeof(char) * (key.Length + (response.ContentType?.Length ?? 0)) + (response.Body?.Length ?? 0);
}

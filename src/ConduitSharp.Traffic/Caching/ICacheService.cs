namespace ConduitSharp.Traffic.Caching;

/// <summary>
/// Read/write cache abstraction used by <see cref="CachePlugin"/>.
/// </summary>
public interface ICacheService
{
    /// <summary>Returns the cached entry for <paramref name="key"/>, or <c>null</c> on a miss.</summary>
    ValueTask<CachedResponse?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Stores <paramref name="response"/> under <paramref name="key"/> for <paramref name="ttl"/>.</summary>
    Task SetAsync(string key, CachedResponse response, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Removes the entry for <paramref name="key"/> if present. Used by cache invalidation
    /// (e.g. the admin API). A no-op when the key is absent.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes every entry whose (logical) key starts with <paramref name="keyPrefix"/> and
    /// returns how many were removed. Used to flush a route's cached responses at once.
    /// </summary>
    Task<int> RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default);
}

/// <summary>
/// A cached upstream response. <see cref="Body"/> is the exact bytes the upstream wrote —
/// never decoded to text — so binary and encoded (e.g. gzip) responses round-trip unchanged.
/// </summary>
public sealed record CachedResponse(int StatusCode, string? ContentType, byte[] Body);

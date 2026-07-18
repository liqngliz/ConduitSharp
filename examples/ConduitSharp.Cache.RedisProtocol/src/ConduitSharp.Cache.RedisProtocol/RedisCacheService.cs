using System.Text.Json;
using ConduitSharp.Traffic.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ConduitSharp.Cache.RedisProtocol;

/// <summary>
/// Distributed response cache <see cref="ICacheService"/> over the Redis protocol (RESP).
/// Works with Valkey, Redis 7, and any RESP-compatible server via StackExchange.Redis. Drop
/// this assembly into the gateway's plugins root and set <c>Gateway:Cache:Redis:ConnectionString</c>;
/// the host discovers it and uses it in place of the built-in in-process cache, so all
/// gateway instances share one cache.
///
/// Fails open: every backend operation is guarded, so an outage degrades the gateway to
/// no-caching (cache misses forwarded to the upstream) rather than failing requests. The
/// connection is created with <c>AbortOnConnectFail=false</c>, so the gateway also starts
/// even when the cache server is temporarily unreachable.
/// </summary>
public sealed class RedisCacheService : ICacheService, IDisposable
{
    private static readonly JsonSerializerOptions Json = new();

    private readonly IConnectionMultiplexer? _mux;
    private readonly IDatabase _db;
    private readonly string _prefix;
    private readonly ILogger<RedisCacheService> _logger;

    // DI constructor: reads its own connection settings straight from configuration —
    // core's GatewayOptions doesn't declare a Redis-shaped property, so this backend's
    // config schema is free to evolve independently of the core package's version.
    public RedisCacheService(IConfiguration configuration, ILogger<RedisCacheService> logger)
    {
        var cfg = configuration.GetSection("Gateway:Cache:Redis").Get<RedisCacheOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(cfg.ConnectionString))
            throw new InvalidOperationException(
                "The Redis cache is installed but 'Gateway:Cache:Redis:ConnectionString' is not set.");

        var redisConfig = ConfigurationOptions.Parse(cfg.ConnectionString);
        redisConfig.AbortOnConnectFail = false; // start even if Redis is momentarily down

        _mux    = ConnectionMultiplexer.Connect(redisConfig);
        _db     = cfg.Database >= 0 ? _mux.GetDatabase(cfg.Database) : _mux.GetDatabase();
        _prefix = cfg.KeyPrefix;
        _logger = logger;
    }

    // Test constructor: inject a database directly.
    internal RedisCacheService(IDatabase db, string keyPrefix, ILogger<RedisCacheService> logger)
    {
        _db     = db;
        _prefix = keyPrefix;
        _logger = logger;
    }

    public async ValueTask<CachedResponse?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _db.StringGetAsync(_prefix + key);
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CachedResponse>((string)value!, Json);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis GET failed for a cache key; treating as a miss.");
            return null;
        }
    }

    public async Task SetAsync(string key, CachedResponse response, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            await _db.StringSetAsync(_prefix + key, JsonSerializer.Serialize(response, Json), ttl);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis SET failed for a cache key; response not cached.");
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _db.KeyDeleteAsync(_prefix + key);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis DEL failed for a cache key.");
        }
    }

    public async Task<int> RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default)
    {
        try
        {
            var pattern = _prefix + keyPrefix + "*";
            var removed = 0;
            // Scan each server (single node in the common case) for matching keys and delete them.
            foreach (var endpoint in _mux!.GetEndPoints())
            {
                var server = _mux.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica) continue;
                foreach (var key in server.Keys(_db.Database, pattern, pageSize: 250))
                    if (await _db.KeyDeleteAsync(key))
                        removed++;
            }
            return removed;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis prefix invalidation failed for '{Prefix}'.", keyPrefix);
            return 0;
        }
    }

    // Redis/transport failures must not surface — the cache degrades to no-op. Serialization
    // bugs (JsonException) are not caught here so they surface in development.
    private static bool IsRedisFailure(Exception ex) =>
        ex is RedisException or TimeoutException or ObjectDisposedException;

    public void Dispose() => _mux?.Dispose();
}

/// <summary>Connection settings bound from the <c>Gateway:Cache:Redis</c> configuration section.</summary>
internal sealed record RedisCacheOptions
{
    /// <summary>StackExchange.Redis connection string, e.g. <c>localhost:6379</c>. Required.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>Prefix applied to every cache key, isolating this gateway's entries. Default: <c>conduitsharp:cache:</c>.</summary>
    public string KeyPrefix { get; init; } = "conduitsharp:cache:";

    /// <summary>Redis database index. Default: <c>-1</c> (the connection's default database).</summary>
    public int Database { get; init; } = -1;
}

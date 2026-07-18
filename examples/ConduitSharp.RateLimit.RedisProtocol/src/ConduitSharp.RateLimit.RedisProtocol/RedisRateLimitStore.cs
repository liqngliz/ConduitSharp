using ConduitSharp.Traffic.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ConduitSharp.RateLimit.RedisProtocol;

/// <summary>
/// Distributed <see cref="IRateLimitStore"/> over the Redis protocol (RESP). Works with
/// Valkey, Redis 7, and any RESP-compatible server via StackExchange.Redis. Drop this
/// assembly into the gateway's plugins root and set
/// <c>Gateway:RateLimiting:Redis:ConnectionString</c>; the host discovers it and uses it in
/// place of the built-in per-process store, so rate limits are enforced across all gateway
/// replicas sharing this backend.
///
/// Fails open: a backend outage degrades to not enforcing the limit (the request is allowed
/// through) rather than rejecting traffic or crashing the gateway. The connection is created
/// with <c>AbortOnConnectFail=false</c>, so the gateway also starts even when the rate-limit
/// server is temporarily unreachable.
/// </summary>
public sealed class RedisRateLimitStore : IRateLimitStore, IDisposable
{
    private const string LuaScript = """
        local key = KEYS[1]
        local max = tonumber(ARGV[1])
        local ttl = tonumber(ARGV[2])
        local current = redis.call('INCR', key)
        if current == 1 then
          redis.call('EXPIRE', key, ttl)
        end
        return current
        """;

    private readonly IConnectionMultiplexer? _connection;
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<RedisRateLimitStore> _logger;

    // DI constructor: reads its own connection settings straight from configuration —
    // core's GatewayOptions doesn't declare a Redis-shaped property, so this backend's
    // config schema is free to evolve independently of the core package's version.
    public RedisRateLimitStore(IConfiguration configuration, ILogger<RedisRateLimitStore> logger)
    {
        var cfg = configuration.GetSection("Gateway:RateLimiting:Redis").Get<RedisRateLimitOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(cfg.ConnectionString))
            throw new InvalidOperationException(
                "The Redis rate-limit store is installed but 'Gateway:RateLimiting:Redis:ConnectionString' is not set.");

        var redisConfig = ConfigurationOptions.Parse(cfg.ConnectionString);
        redisConfig.AbortOnConnectFail = false; // start even if Redis is momentarily down

        _connection = ConnectionMultiplexer.Connect(redisConfig);
        _database   = cfg.Database >= 0 ? _connection.GetDatabase(cfg.Database) : _connection.GetDatabase();
        _keyPrefix  = cfg.KeyPrefix;
        _logger     = logger;
    }

    // Test constructor: inject a database directly.
    internal RedisRateLimitStore(IDatabase database, string keyPrefix, ILogger<RedisRateLimitStore> logger)
    {
        _database  = database;
        _keyPrefix = keyPrefix;
        _logger    = logger;
    }

    public bool TryAcquire(string key, long windowId, int windowSeconds, long maxRequests)
    {
        try
        {
            var redisKey = _keyPrefix + key + ":" + windowId;
            var result = _database.ScriptEvaluate(
                LuaScript,
                [new RedisKey(redisKey)],
                [new RedisValue(maxRequests.ToString()), new RedisValue(windowSeconds.ToString())]);

            return long.TryParse(result.ToString(), out var current) && current <= maxRequests;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis rate-limit check failed; allowing the request through.");
            return true;
        }
    }

    private static bool IsRedisFailure(Exception ex) =>
        ex is RedisException or TimeoutException or ObjectDisposedException;

    public void Dispose() => _connection?.Dispose();
}

/// <summary>Connection settings bound from the <c>Gateway:RateLimiting:Redis</c> configuration section.</summary>
internal sealed record RedisRateLimitOptions
{
    /// <summary>StackExchange.Redis connection string, e.g. <c>localhost:6379</c>. Required.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>Prefix applied to every rate-limit key, isolating this gateway's counters. Default: <c>conduitsharp:ratelimit:</c>.</summary>
    public string KeyPrefix { get; init; } = "conduitsharp:ratelimit:";

    /// <summary>Redis database index. Default: <c>-1</c> (the connection's default database).</summary>
    public int Database { get; init; } = -1;
}

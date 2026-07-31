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

    private const string AddScript = """
        local key = KEYS[1]
        local amount = tonumber(ARGV[1])
        local ttl = tonumber(ARGV[2])
        local current = redis.call('INCRBY', key, amount)
        if current == amount then
          redis.call('EXPIRE', key, ttl)
        end
        return current
        """;

    private readonly IConnectionMultiplexer? _connection;
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<RedisRateLimitStore> _logger;

    public RedisRateLimitStore(IConfiguration configuration, ILogger<RedisRateLimitStore> logger)
    {
        var cfg = configuration.GetSection("Gateway:RateLimiting:Redis").Get<RedisRateLimitOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(cfg.ConnectionString))
            throw new InvalidOperationException(
                "The Redis rate-limit store is installed but 'Gateway:RateLimiting:Redis:ConnectionString' is not set.");

        var redisConfig = ConfigurationOptions.Parse(cfg.ConnectionString);
        redisConfig.AbortOnConnectFail = false;

        _connection = ConnectionMultiplexer.Connect(redisConfig);
        _database   = cfg.Database >= 0 ? _connection.GetDatabase(cfg.Database) : _connection.GetDatabase();
        _keyPrefix  = cfg.KeyPrefix;
        _logger     = logger;
    }

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

    public long Add(string key, long windowId, int windowSeconds, long amount)
    {
        try
        {
            var redisKey = _keyPrefix + key + ":" + windowId;
            var result = _database.ScriptEvaluate(
                AddScript,
                [new RedisKey(redisKey)],
                [new RedisValue(amount.ToString()), new RedisValue(windowSeconds.ToString())]);

            return long.TryParse(result.ToString(), out var current) ? current : 0;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis token charge failed; the tokens go uncounted.");
            return 0;
        }
    }

    public long Peek(string key, long windowId)
    {
        try
        {
            var value = _database.StringGet(_keyPrefix + key + ":" + windowId);
            return value.HasValue && long.TryParse(value.ToString(), out var current) ? current : 0;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis token peek failed; allowing the request through.");
            return 0;
        }
    }

    private static bool IsRedisFailure(Exception ex) =>
        ex is RedisException or TimeoutException or ObjectDisposedException;

    public void Dispose() => _connection?.Dispose();
}

namespace ConduitSharp.RateLimit.RedisProtocol;


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

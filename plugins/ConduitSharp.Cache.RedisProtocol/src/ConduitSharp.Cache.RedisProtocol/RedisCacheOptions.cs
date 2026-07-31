namespace ConduitSharp.Cache.RedisProtocol;


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

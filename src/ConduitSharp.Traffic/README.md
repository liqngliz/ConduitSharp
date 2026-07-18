# ConduitSharp.Traffic

Rate limiting, caching, and traffic-shaping abstractions for ConduitSharp plugins.

Provides interfaces for building custom rate-limit stores and cache backends that work with the gateway:

- `IRateLimitStore` — distributed rate-limit counter backend
- `ICacheService` — distributed response cache

Implement these interfaces to plug in your own Redis, memcached, DynamoDB, or any other backend.

## Example: Redis Rate Limiter

```csharp
public class RedisRateLimitStore : IRateLimitStore
{
    private readonly IConnectionMultiplexer redis;
    
    public async Task<bool> CheckAsync(string key, int limit, TimeSpan window)
    {
        var db = redis.GetDatabase();
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, window);
        return count <= limit;
    }
}
```

See [ConduitSharp.RateLimit.RedisProtocol](https://www.nuget.org/packages/ConduitSharp.RateLimit.RedisProtocol) for a complete implementation.

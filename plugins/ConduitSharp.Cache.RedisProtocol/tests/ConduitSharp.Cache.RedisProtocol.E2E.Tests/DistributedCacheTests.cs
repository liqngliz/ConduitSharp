using System.Text;
using ConduitSharp.Traffic.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ConduitSharp.Cache.RedisProtocol.E2E.Tests;

/// <summary>
/// Distributed-cache behaviour, run against both a real Redis and a real Valkey (see the two
/// concrete subclasses below). Each RedisCacheService instance stands in for a separate
/// gateway process; sharing one backend is what makes the cache distributed.
/// </summary>
public abstract class DistributedCacheTests(CacheServerFixture fx)
{
    private static IConfiguration Config(string connection, string prefix) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gateway:Cache:Redis:ConnectionString"] = connection,
            ["Gateway:Cache:Redis:KeyPrefix"]         = prefix,
        }).Build();

    private RedisCacheService Service(string connection, string prefix = "cs:e2e:") =>
        new(Config(connection, prefix), NullLogger<RedisCacheService>.Instance);

    [Fact]
    public async Task CacheSetByOneInstance_IsServedByAnother()
    {
        if (!fx.Available) return;

        // Two independent services (= two gateway instances) sharing one Redis.
        using var instanceA = Service(fx.ConnectionString);
        using var instanceB = Service(fx.ConnectionString);

        var key      = $"route\0/shared-{Guid.NewGuid():N}";
        var response = new CachedResponse(200, "application/json", Encoding.UTF8.GetBytes("""{"from":"A"}"""));

        await instanceA.SetAsync(key, response, TimeSpan.FromMinutes(5));
        var seenByB = await instanceB.GetAsync(key);

        Assert.NotNull(seenByB);
        Assert.Equal(200, seenByB!.StatusCode);
        Assert.Equal("""{"from":"A"}""", Encoding.UTF8.GetString(seenByB.Body));
    }

    [Fact]
    public async Task RemoveByPrefix_ScansAndDeletesRealKeys()
    {
        if (!fx.Available) return;

        var prefix = $"cs:e2e:{Guid.NewGuid():N}:";
        using var svc = Service(fx.ConnectionString, prefix);

        await svc.SetAsync("route-a\0/x", new CachedResponse(200, null, Encoding.UTF8.GetBytes("a1")), TimeSpan.FromMinutes(5));
        await svc.SetAsync("route-a\0/y", new CachedResponse(200, null, Encoding.UTF8.GetBytes("a2")), TimeSpan.FromMinutes(5));
        await svc.SetAsync("route-b\0/x", new CachedResponse(200, null, Encoding.UTF8.GetBytes("b1")), TimeSpan.FromMinutes(5));

        var removed = await svc.RemoveByPrefixAsync("route-a\0");

        Assert.Equal(2, removed);
        Assert.Null(await svc.GetAsync("route-a\0/x"));
        Assert.Null(await svc.GetAsync("route-a\0/y"));
        Assert.NotNull(await svc.GetAsync("route-b\0/x"));
    }

    [Fact]
    public async Task Ttl_Expires()
    {
        if (!fx.Available) return;

        using var svc = Service(fx.ConnectionString);
        var key = $"route\0/ttl-{Guid.NewGuid():N}";

        await svc.SetAsync(key, new CachedResponse(200, null, Encoding.UTF8.GetBytes("x")), TimeSpan.FromSeconds(1));
        Assert.NotNull(await svc.GetAsync(key));

        await Task.Delay(1500);
        Assert.Null(await svc.GetAsync(key));
    }
}

// The same distributed tests, executed against each backend.
[Trait("Category", "E2E")]
[Collection("Redis Cache E2E")]
public sealed class RedisDistributedCacheTests(RedisFixture fx) : DistributedCacheTests(fx);

[Trait("Category", "E2E")]
[Collection("Valkey Cache E2E")]
public sealed class ValkeyDistributedCacheTests(ValkeyFixture fx) : DistributedCacheTests(fx);

/// <summary>
/// Fail-open against an unreachable server — needs no Docker, so it is not in a backend
/// collection and runs everywhere.
/// </summary>
public sealed class RedisCacheFailOpenTests
{
    [Fact]
    public async Task DeadRedis_FailsOpen_NoThrow()
    {
        using var svc = new RedisCacheService(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Cache:Redis:ConnectionString"] = "127.0.0.1:6390,connectTimeout=500,connectRetry=0",
                ["Gateway:Cache:Redis:KeyPrefix"]         = "cs:e2e:",
            }).Build(),
            NullLogger<RedisCacheService>.Instance);

        var key = $"route\0/dead-{Guid.NewGuid():N}";
        await svc.SetAsync(key, new CachedResponse(200, null, Encoding.UTF8.GetBytes("x")), TimeSpan.FromMinutes(1)); // swallowed
        Assert.Null(await svc.GetAsync(key));                       // treated as a miss
        Assert.Equal(0, await svc.RemoveByPrefixAsync("route\0"));  // no-op
    }
}

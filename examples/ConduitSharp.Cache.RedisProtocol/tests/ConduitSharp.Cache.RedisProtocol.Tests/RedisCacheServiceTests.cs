using System.Text;
using System.Text.Json;
using ConduitSharp.Traffic.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace ConduitSharp.Cache.RedisProtocol.Tests;

public sealed class RedisCacheServiceTests
{
    private const string Prefix = "cs:cache:";

    private static (RedisCacheService Svc, IDatabase Db) Build()
    {
        var db  = Substitute.For<IDatabase>();
        var svc = new RedisCacheService(db, Prefix, NullLogger<RedisCacheService>.Instance);
        return (svc, db);
    }

    [Fact]
    public async Task Get_Hit_DeserializesResponse()
    {
        var (svc, db) = Build();
        var body = Encoding.UTF8.GetBytes("""{"ok":true}""");
        var stored = JsonSerializer.Serialize(new CachedResponse(200, "application/json", body));
        db.StringGetAsync(Prefix + "k").Returns(new RedisValue(stored));

        var result = await svc.GetAsync("k");

        Assert.NotNull(result);
        Assert.Equal(200, result!.StatusCode);
        Assert.Equal("application/json", result.ContentType);
        Assert.Equal(body, result.Body);
    }

    [Fact]
    public async Task Get_Miss_ReturnsNull()
    {
        var (svc, db) = Build();
        db.StringGetAsync(Prefix + "k").Returns(RedisValue.Null);

        Assert.Null(await svc.GetAsync("k"));
    }

    [Fact]
    public async Task Set_SerializesWithPrefixAndTtl()
    {
        var (svc, db) = Build();
        var ttl = TimeSpan.FromSeconds(30);

        await svc.SetAsync("k", new CachedResponse(200, "text/plain", Encoding.UTF8.GetBytes("hello")), ttl);

        await db.Received(1).StringSetAsync(
            Prefix + "k",
            Arg.Is<RedisValue>(v => JsonSerializer.Deserialize<CachedResponse>((string)v!, (JsonSerializerOptions?)null)!.Body.SequenceEqual(Encoding.UTF8.GetBytes("hello"))),
            ttl,
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task Remove_DeletesPrefixedKey()
    {
        var (svc, db) = Build();

        await svc.RemoveAsync("k");

        await db.Received(1).KeyDeleteAsync(Prefix + "k", Arg.Any<CommandFlags>());
    }

    // ------------------------------------------------------------------
    // Fail-open — Redis errors degrade to no-cache, never surface
    // ------------------------------------------------------------------

    [Fact]
    public async Task Get_RedisFailure_ReturnsNullNotThrow()
    {
        var (svc, db) = Build();
        db.StringGetAsync(Arg.Any<RedisKey>())
          .Returns<Task<RedisValue>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        Assert.Null(await svc.GetAsync("k")); // treated as a miss
    }

    [Fact]
    public async Task Set_RedisFailure_SwallowedNotThrow()
    {
        var (svc, db) = Build();
        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
          .Returns<Task<bool>>(_ => throw new RedisTimeoutException("slow", CommandStatus.Unknown));

        await svc.SetAsync("k", new CachedResponse(200, null, Encoding.UTF8.GetBytes("x")), TimeSpan.FromSeconds(5)); // must not throw
    }
}

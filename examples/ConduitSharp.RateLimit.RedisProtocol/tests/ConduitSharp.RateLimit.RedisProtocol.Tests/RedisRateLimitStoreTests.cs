using ConduitSharp.RateLimit.RedisProtocol;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace ConduitSharp.RateLimit.RedisProtocol.Tests;

public sealed class RedisRateLimitStoreTests
{
    private const string Prefix = "rl:";

    private static (RedisRateLimitStore Store, IDatabase Db) Build()
    {
        var db    = Substitute.For<IDatabase>();
        var store = new RedisRateLimitStore(db, Prefix, NullLogger<RedisRateLimitStore>.Instance);
        return (store, db);
    }

    [Fact]
    public void TryAcquire_UnderLimit_Allows()
    {
        var (store, db) = Build();
        db.ScriptEvaluate(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue("1")));

        Assert.True(store.TryAcquire("client", windowId: 1, windowSeconds: 60, maxRequests: 1));
    }

    [Fact]
    public void TryAcquire_OverLimit_Blocks()
    {
        var (store, db) = Build();
        db.ScriptEvaluate(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue("2")));

        Assert.False(store.TryAcquire("client", windowId: 1, windowSeconds: 60, maxRequests: 1));
    }

    [Fact]
    public void TryAcquire_RedisUnreachable_FailsOpenAndAllows()
    {
        var (store, db) = Build();
        db.ScriptEvaluate(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(_ => throw new TimeoutException("redis timed out"));

        Assert.True(store.TryAcquire("client", windowId: 1, windowSeconds: 60, maxRequests: 1));
    }
}

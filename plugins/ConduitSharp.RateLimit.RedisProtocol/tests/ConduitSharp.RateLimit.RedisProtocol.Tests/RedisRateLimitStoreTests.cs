using ConduitSharp.RateLimit.RedisProtocol;
using Microsoft.Extensions.Configuration;
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

    // Add and Peek are the token-metering path: token-rate-limit charges through Add and gates on
    // Peek, so a silent break here would let budgets run unenforced across replicas.

    [Fact]
    public void Add_ReturnsTheRunningWindowTotal_AndKeysByWindow()
    {
        var (store, db) = Build();
        db.ScriptEvaluate(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue("150")));

        Assert.Equal(150, store.Add("client", windowId: 7, windowSeconds: 60, amount: 110));

        db.Received(1).ScriptEvaluate(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(k => k.Length == 1 && k[0] == (RedisKey)"rl:client:7"),
            Arg.Is<RedisValue[]>(v => v.Length == 2 && v[0] == "110" && v[1] == "60"));
    }

    [Fact]
    public void Add_RedisUnreachable_ReturnsZeroSoTokensGoUncounted()
    {
        var (store, db) = Build();
        db.ScriptEvaluate(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(_ => throw new TimeoutException("redis timed out"));

        Assert.Equal(0, store.Add("client", windowId: 1, windowSeconds: 60, amount: 500));
    }

    [Fact]
    public void Peek_ReadsTheWindowCounter()
    {
        var (store, db) = Build();
        db.StringGet((RedisKey)"rl:client:7", Arg.Any<CommandFlags>()).Returns(new RedisValue("4200"));

        Assert.Equal(4200, store.Peek("client", windowId: 7));
    }

    [Fact]
    public void Peek_MissingKey_ReadsZero()
    {
        var (store, db) = Build();
        db.StringGet(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

        Assert.Equal(0, store.Peek("client", windowId: 7));
    }

    [Fact]
    public void Peek_RedisUnreachable_FailsOpenAsZero()
    {
        var (store, db) = Build();
        db.StringGet(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(_ => throw new RedisTimeoutException("redis timed out", CommandStatus.Unknown));

        Assert.Equal(0, store.Peek("client", windowId: 7));
    }

    [Fact]
    public void NonRedisExceptions_Propagate_RatherThanFailingOpenSilently()
    {
        // Fail-open covers backend outages. A bug in our own code must still surface.
        var (store, db) = Build();
        db.ScriptEvaluate(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(_ => throw new InvalidOperationException("boom"));

        Assert.Throws<InvalidOperationException>(() => store.TryAcquire("c", 1, 60, 1));
        Assert.Throws<InvalidOperationException>(() => store.Add("c", 1, 60, 1));
    }

    [Fact]
    public void DisposingATestConstructedStore_DoesNotThrow()
    {
        // The connection is null on this path; Dispose must tolerate it.
        var (store, _) = Build();
        store.Dispose();
    }

    [Fact]
    public void MissingConnectionString_FailsAtConstructionWithAnActionableMessage()
    {
        var empty = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(
            () => new RedisRateLimitStore(empty, NullLogger<RedisRateLimitStore>.Instance));

        Assert.Contains("Gateway:RateLimiting:Redis:ConnectionString", ex.Message, StringComparison.Ordinal);
    }
}

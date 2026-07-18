using System.Text;
using Xunit;
using ConduitSharp.Traffic.Caching;

namespace ConduitSharp.Traffic.Tests;

public sealed class InMemoryCacheServiceTests
{
    private readonly InMemoryCacheService _cache = new();
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static CachedResponse Response(int status = 200, string body = "ok") =>
        new(status, "text/plain", Encoding.UTF8.GetBytes(body));

    private static string Text(CachedResponse response) => Encoding.UTF8.GetString(response.Body);

    // -------------------------------------------------------------------------
    // Miss
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_EmptyCache_ReturnsNull()
    {
        var result = await _cache.GetAsync("missing-key");

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // Hit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetThenGet_ReturnsCachedEntry()
    {
        var response = Response(200, "hello");
        await _cache.SetAsync("k1", response, OneMinute);

        var hit = await _cache.GetAsync("k1");

        Assert.NotNull(hit);
        Assert.Equal(200,         hit.StatusCode);
        Assert.Equal("text/plain", hit.ContentType);
        Assert.Equal("hello",     Text(hit));
    }

    [Fact]
    public async Task SetThenGet_DifferentKeys_IndependentEntries()
    {
        await _cache.SetAsync("a", Response(200, "a-body"), OneMinute);
        await _cache.SetAsync("b", Response(201, "b-body"), OneMinute);

        Assert.Equal("a-body", Text((await _cache.GetAsync("a"))!));
        Assert.Equal("b-body", Text((await _cache.GetAsync("b"))!));
    }

    // -------------------------------------------------------------------------
    // Expired entry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ExpiredEntry_ReturnsNull()
    {
        await _cache.SetAsync("expired", Response(), TimeSpan.FromMilliseconds(-1));

        var result = await _cache.GetAsync("expired");

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // Overwrite
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Set_OverwritesExistingKey()
    {
        await _cache.SetAsync("key", Response(200, "old"), OneMinute);
        await _cache.SetAsync("key", Response(200, "new"), OneMinute);

        var hit = await _cache.GetAsync("key");

        Assert.Equal("new", Text(hit!));
    }

    // -------------------------------------------------------------------------
    // Remove (cache invalidation)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Remove_DeletesEntry()
    {
        await _cache.SetAsync("key", Response(200, "cached"), OneMinute);
        await _cache.RemoveAsync("key");

        Assert.Null(await _cache.GetAsync("key"));
    }

    [Fact]
    public async Task Remove_MissingKey_IsNoOp()
    {
        await _cache.RemoveAsync("never-set"); // must not throw
    }

    [Fact]
    public async Task RemoveByPrefix_RemovesMatchingKeysOnly()
    {
        await _cache.SetAsync("route-a\0/x", Response(200, "a1"), OneMinute);
        await _cache.SetAsync("route-a\0/y", Response(200, "a2"), OneMinute);
        await _cache.SetAsync("route-b\0/x", Response(200, "b1"), OneMinute);

        var removed = await _cache.RemoveByPrefixAsync("route-a\0");

        Assert.Equal(2, removed);
        Assert.Null(await _cache.GetAsync("route-a\0/x"));
        Assert.Null(await _cache.GetAsync("route-a\0/y"));
        Assert.NotNull(await _cache.GetAsync("route-b\0/x")); // untouched
    }

    // -------------------------------------------------------------------------
    // Total-bytes cap — cache keys are attacker-controlled, so the cache must
    // not grow without bound (mirrors the request-body budget).
    // -------------------------------------------------------------------------

    private static long SizeOf(string key, string body) =>
        sizeof(char) * (key.Length + "text/plain".Length) + Encoding.UTF8.GetByteCount(body);

    [Fact]
    public async Task Set_OverBudget_EvictsEntryClosestToExpiry()
    {
        var body = new string('x', 100);
        // Budget fits exactly two entries.
        var cache = new InMemoryCacheService(2 * SizeOf("k1", body));

        await cache.SetAsync("k1", Response(200, body), TimeSpan.FromMinutes(1)); // expires first
        await cache.SetAsync("k2", Response(200, body), TimeSpan.FromMinutes(10));
        await cache.SetAsync("k3", Response(200, body), TimeSpan.FromMinutes(10)); // over budget

        Assert.Null(await cache.GetAsync("k1"));    // evicted (closest to expiry)
        Assert.NotNull(await cache.GetAsync("k2"));
        Assert.NotNull(await cache.GetAsync("k3"));
    }

    [Fact]
    public async Task Set_EntryLargerThanBudget_IsNotCached_AndEvictsNothing()
    {
        var cache = new InMemoryCacheService(maxTotalBytes: 64);

        await cache.SetAsync("small", Response(200, "s"), OneMinute);
        await cache.SetAsync("huge", Response(200, new string('x', 10_000)), OneMinute);

        Assert.Null(await cache.GetAsync("huge"));
        Assert.NotNull(await cache.GetAsync("small")); // untouched by the oversized write
    }

    [Fact]
    public async Task Set_ManyDistinctKeys_TotalStaysBounded()
    {
        var body  = new string('x', 100);
        var cache = new InMemoryCacheService(4 * SizeOf("k000", body));

        // Simulates ?x=1, ?x=2, … key stuffing: far more entries than the budget holds.
        for (var i = 0; i < 100; i++)
            await cache.SetAsync($"k{i:D3}", Response(200, body), OneMinute);

        var alive = 0;
        for (var i = 0; i < 100; i++)
            if (await cache.GetAsync($"k{i:D3}") is not null)
                alive++;

        Assert.InRange(alive, 1, 4);
    }

    [Fact]
    public async Task Set_ZeroBudget_DisablesCap()
    {
        var cache = new InMemoryCacheService(maxTotalBytes: 0);

        for (var i = 0; i < 50; i++)
            await cache.SetAsync($"k{i}", Response(200, new string('x', 1000)), OneMinute);

        Assert.NotNull(await cache.GetAsync("k0"));
        Assert.NotNull(await cache.GetAsync("k49"));
    }

    [Fact]
    public async Task RemoveAndOverwrite_ReleaseBudget()
    {
        var body  = new string('x', 100);
        var cache = new InMemoryCacheService(2 * SizeOf("k1", body));

        // Churn the same two keys: releases must balance reserves or writes start evicting.
        for (var i = 0; i < 20; i++)
        {
            await cache.SetAsync("k1", Response(200, body), OneMinute);
            await cache.SetAsync("k2", Response(200, body), OneMinute);
            await cache.RemoveAsync("k2");
        }

        Assert.NotNull(await cache.GetAsync("k1")); // never evicted — budget was never truly exceeded
    }
}

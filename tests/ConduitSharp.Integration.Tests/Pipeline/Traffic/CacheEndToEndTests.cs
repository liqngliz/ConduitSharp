using System.Text;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Pipeline.Traffic;

public sealed class CacheEndToEndTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    private string Routes(long maxCacheableBytes = 1024 * 1024) =>
        GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "cache",
            new { ttlSeconds = 60, varyByHeaders = Array.Empty<string>(), maxCacheableBytes });

    [Fact]
    public async Task GetCacheMiss_ForwardsToUpstream()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task GetCacheHit_SecondRequestServedFromCache()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        // First request — cache miss, upstream is called and response is stored.
        var first = await client.GetAsync("/items");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);

        // Second request — cache hit, upstream must NOT be called again.
        var second = await client.GetAsync("/items");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(_upstream.ReceivedRequests); // still only 1
        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());
    }

    [Fact]
    public async Task GetCacheHit_PreservesUpstreamContentType()
    {
        // Regression: WriteShortCircuitAsync used to clobber the cached Content-Type
        // with text/plain after copying ShortCircuitHeaders.
        _upstream.RespondWith(200, """{"ok":true}""", "application/json");

        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        await client.GetAsync("/items");
        var second = await client.GetAsync("/items");

        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());
        Assert.Equal("application/json", second.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("no-store")]
    [InlineData("private, max-age=60")]
    public async Task UpstreamCacheControlOptOut_ResponseIsNotCached(string cacheControl)
    {
        // The cache is explicit-opt-in per route, but an upstream that marks a response
        // no-store (or private — this gateway is a shared cache) must still win.
        _upstream.RespondWith(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.CacheControl = cacheControl;
            await ctx.Response.WriteAsync("sensitive");
        });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        await client.GetAsync("/items");
        var second = await client.GetAsync("/items");

        Assert.Equal("sensitive", await second.Content.ReadAsStringAsync());
        Assert.False(second.Headers.Contains("X-Cache"));   // not served from cache
        Assert.Equal(2, _upstream.ReceivedRequests.Count);  // both requests hit the upstream
    }

    [Fact]
    public async Task PostRequest_BypassesCache_AlwaysForwardsToUpstream()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        await client.PostAsync("/items", new StringContent("{}", Encoding.UTF8, "application/json"));
        await client.PostAsync("/items", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(2, _upstream.ReceivedRequests.Count);
    }

    [Fact]
    public async Task GetCacheMiss_UpstreamNonSuccess_ResponseIsForwarded_AndNotCached()
    {
        // Upstream returns 500 — the cache callback must NOT be invoked, so the
        // second request must still reach the upstream (nothing was cached).
        _upstream.RespondWith(500, "Internal error");

        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        var first = await client.GetAsync("/items");
        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);

        var second = await client.GetAsync("/items");
        Assert.Equal(HttpStatusCode.InternalServerError, second.StatusCode);

        // Both requests must have reached the upstream — the 500 was NOT cached.
        Assert.Equal(2, _upstream.ReceivedRequests.Count);
    }

    // -------------------------------------------------------------------------
    // R4 — response is streamed and captured together (tee), with a size cap
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LargeResponse_IsCachedWithoutMemoryBlowup()
    {
        // A 256 KB body (under the 1 MiB default cap) is streamed to the client and
        // captured at the same time — served intact on the miss and identically on the hit.
        var big = new string('x', 256 * 1024);
        _upstream.RespondWith(200, big, "text/plain");

        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        var first = await client.GetAsync("/big");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(big, await first.Content.ReadAsStringAsync()); // streamed intact

        var second = await client.GetAsync("/big");
        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());
        Assert.Equal(big, await second.Content.ReadAsStringAsync()); // cached intact
        Assert.Single(_upstream.ReceivedRequests);                   // served from cache
    }

    // Coalescing requires the response-producing plugin (here http-proxy) to run *inside*
    // the chain, so the cache plugin's next() encompasses the upstream call.
    private string CoalesceRoutes() => $$"""
        {
          "routes": [{
            "id": "coalesce-route",
            "route": { "match": { "path": "/{**rest}" } },
            "cluster": {
              "loadBalancingPolicy": "RoundRobin",
              "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } },
              "httpRequest": { "activityTimeout": "00:00:05" }
            },
            "plugins": [
              { "name": "cache",      "order": 1, "enabled": true, "config": { "ttlSeconds": 300 } },
              { "name": "http-proxy", "order": 2, "enabled": true, "config": {} }
            ]
          }]
        }
        """;

    [Fact]
    public async Task ConcurrentMisses_AreCoalesced_UpstreamCalledOnce()
    {
        // Stampede protection: a slow upstream + many concurrent misses for the same key
        // must collapse to a single upstream request; the rest share the leader's result.
        var calls = 0;
        _upstream.RespondWith(async ctx =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(400); // hold the leader long enough for followers to queue
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("ok");
        });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, CoalesceRoutes());
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => client.GetAsync("/coalesce")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, calls);                      // 9 followers coalesced onto 1 leader
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task ResponseOverCacheLimit_IsStreamedButNotCached()
    {
        // A 4 KB body with a 1 KB cache cap: the client still gets the full body
        // (streaming is never interrupted), but capture stops past the cap so nothing
        // is cached — the second request re-fetches from the upstream.
        var body = new string('y', 4096);
        _upstream.RespondWith(200, body, "text/plain");

        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes(maxCacheableBytes: 1024));
        using var client = factory.CreateClient();

        var first = await client.GetAsync("/big");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(body, await first.Content.ReadAsStringAsync()); // full body despite exceeding cap

        var second = await client.GetAsync("/big");
        Assert.Equal(body, await second.Content.ReadAsStringAsync());
        Assert.False(second.Headers.Contains("X-Cache"));            // not a cache hit
        Assert.Equal(2, _upstream.ReceivedRequests.Count);          // re-fetched, not cached
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task Same_plugin_on_four_routes_keeps_separate_configs()
    {
        // Four distinct cache configs; each route must behave per its OWN config:
        //   a: normal cache            -> 2nd GET is a HIT
        //   b: maxCacheableBytes = 1   -> nothing cacheable, 2nd GET re-fetches
        //   c: varyByHeaders X-Tenant  -> different tenant misses, same tenant hits
        //   d: normal cache            -> HIT (b's byte cap must not bleed here)
        _upstream.RespondWith(200, "payload", "text/plain");
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "cache",
            new { ttlSeconds = 60 },
            new { ttlSeconds = 60, maxCacheableBytes = 1 },
            new { ttlSeconds = 60, varyByHeaders = new[] { "X-Tenant" } },
            new { ttlSeconds = 60 });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        async Task<HttpResponseMessage> Get(string path, string? tenant = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (tenant is not null) request.Headers.Add("X-Tenant", tenant);
            return await client.SendAsync(request);
        }

        // a: miss then hit — 1 upstream call.
        await Get("/a/items");
        var aSecond = await Get("/a/items");
        Assert.Equal("HIT", aSecond.Headers.GetValues("X-Cache").Single());
        Assert.Single(_upstream.ReceivedRequests);

        // b: byte cap of 1 makes the body uncacheable — 2 upstream calls, no hit.
        await Get("/b/items");
        var bSecond = await Get("/b/items");
        Assert.False(bSecond.Headers.Contains("X-Cache"));
        Assert.Equal(3, _upstream.ReceivedRequests.Count);

        // c: same path, different X-Tenant — separate cache entries (2 upstream calls),
        // then a tenant repeat is a hit.
        await Get("/c/items", tenant: "t1");
        await Get("/c/items", tenant: "t2");
        Assert.Equal(5, _upstream.ReceivedRequests.Count);
        var cRepeat = await Get("/c/items", tenant: "t1");
        Assert.Equal("HIT", cRepeat.Headers.GetValues("X-Cache").Single());
        Assert.Equal(5, _upstream.ReceivedRequests.Count);

        // d: caches normally — b's maxCacheableBytes=1 did not overwrite this route.
        await Get("/d/items");
        var dSecond = await Get("/d/items");
        Assert.Equal("HIT", dSecond.Headers.GetValues("X-Cache").Single());
        Assert.Equal(6, _upstream.ReceivedRequests.Count);
    }
}

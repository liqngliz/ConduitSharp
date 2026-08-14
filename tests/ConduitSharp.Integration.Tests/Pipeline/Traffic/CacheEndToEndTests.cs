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

        var first = await client.GetAsync("/items");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);

        var second = await client.GetAsync("/items");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());
    }

    [Fact]
    public async Task GetCacheHit_PreservesUpstreamContentType()
    {
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
        Assert.False(second.Headers.Contains("X-Cache"));
        Assert.Equal(2, _upstream.ReceivedRequests.Count);
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
        _upstream.RespondWith(500, "Internal error");

        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        var first = await client.GetAsync("/items");
        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);

        var second = await client.GetAsync("/items");
        Assert.Equal(HttpStatusCode.InternalServerError, second.StatusCode);

        Assert.Equal(2, _upstream.ReceivedRequests.Count);
    }

    [Fact]
    public async Task LargeResponse_IsCachedWithoutMemoryBlowup()
    {
        var big = new string('x', 256 * 1024);
        _upstream.RespondWith(200, big, "text/plain");

        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        var first = await client.GetAsync("/big");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(big, await first.Content.ReadAsStringAsync());

        var second = await client.GetAsync("/big");
        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());
        Assert.Equal(big, await second.Content.ReadAsStringAsync());
        Assert.Single(_upstream.ReceivedRequests);
    }

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
        var calls = 0;
        _upstream.RespondWith(async ctx =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(400);
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("ok");
        });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, CoalesceRoutes());
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => client.GetAsync("/coalesce")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, calls);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task ResponseOverCacheLimit_IsStreamedButNotCached()
    {
        var body = new string('y', 4096);
        _upstream.RespondWith(200, body, "text/plain");

        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes(maxCacheableBytes: 1024));
        using var client = factory.CreateClient();

        var first = await client.GetAsync("/big");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(body, await first.Content.ReadAsStringAsync());

        var second = await client.GetAsync("/big");
        Assert.Equal(body, await second.Content.ReadAsStringAsync());
        Assert.False(second.Headers.Contains("X-Cache"));
        Assert.Equal(2, _upstream.ReceivedRequests.Count);
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task Same_plugin_on_four_routes_keeps_separate_configs()
    {
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

        await Get("/a/items");
        var aSecond = await Get("/a/items");
        Assert.Equal("HIT", aSecond.Headers.GetValues("X-Cache").Single());
        Assert.Single(_upstream.ReceivedRequests);

        await Get("/b/items");
        var bSecond = await Get("/b/items");
        Assert.False(bSecond.Headers.Contains("X-Cache"));
        Assert.Equal(3, _upstream.ReceivedRequests.Count);

        await Get("/c/items", tenant: "t1");
        await Get("/c/items", tenant: "t2");
        Assert.Equal(5, _upstream.ReceivedRequests.Count);
        var cRepeat = await Get("/c/items", tenant: "t1");
        Assert.Equal("HIT", cRepeat.Headers.GetValues("X-Cache").Single());
        Assert.Equal(5, _upstream.ReceivedRequests.Count);

        await Get("/d/items");
        var dSecond = await Get("/d/items");
        Assert.Equal("HIT", dSecond.Headers.GetValues("X-Cache").Single());
        Assert.Equal(6, _upstream.ReceivedRequests.Count);
    }
}

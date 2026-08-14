using ConduitSharp.Gateway.Routing;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Verifies that each supported load-balancing strategy selects an upstream
/// node and forwards the request.
/// </summary>
public sealed class LoadBalancingTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    [Fact]
    public async Task RoundRobin_ForwardsToUpstream()
    {
        var routes = GatewayTestHelpers.CatchAllRoutes(_upstream.BaseUrl, lbStrategy: "RoundRobin");
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RoundRobin_MultipleNodes_DistributesAcrossAllNodes()
    {
        await using var upstream2 = await FakeUpstream.StartAsync();

        var routes = $$"""
            {
              "routes": [{
                "id": "lb-route",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" }, "node-1": { "address": "{{upstream2.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        for (var i = 0; i < 4; i++)
            await client.GetAsync("/ping");

        Assert.Equal(2, _upstream.ReceivedRequests.Count);
        Assert.Equal(2, upstream2.ReceivedRequests.Count);
    }

    [Fact]
    public async Task Random_ForwardsToUpstream()
    {
        var routes = GatewayTestHelpers.CatchAllRoutes(_upstream.BaseUrl, lbStrategy: "Random");
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Theory]
    [InlineData("PowerOfTwoChoices")]
    [InlineData("LeastRequests")]
    [InlineData("FirstAlphabetical")]
    public async Task YarpBuiltInPolicies_AreUsableByName(string strategy)
    {
        var routes = GatewayTestHelpers.CatchAllRoutes(_upstream.BaseUrl, lbStrategy: strategy);
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task UnknownLoadBalancingStrategy_FailsTheGatewayAtStartup_ListingWhatIsAvailable()
    {
        var routes = GatewayTestHelpers.CatchAllRoutes(_upstream.BaseUrl, lbStrategy: "RoundRobbin");
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/api/hello");
        });

        var message = ex.ToString();
        Assert.Contains("RoundRobbin", message, StringComparison.Ordinal);
        Assert.Contains("test-route", message, StringComparison.Ordinal);
        Assert.Contains("RoundRobin", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadBalancingPolicy_EnumRoundTripsToThePolicyName()
    {
        var routes = GatewayTestHelpers.CatchAllRoutes(
            _upstream.BaseUrl, lbStrategy: LoadBalancingPolicy.LeastRequests.ToString());
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task DeadNode_IsCircuitBreakerOpenAfterFailures()
    {
        _upstream.RespondWith(ctx => { ctx.Response.StatusCode = 503; return Task.CompletedTask; });
        await using var healthy = await FakeUpstream.StartAsync();
        healthy.RespondWith(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("ok");
        });

        var routes = $$"""
            {
              "routes": [{
                "id": "cb-route",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" }, "node-1": { "address": "{{healthy.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "retry": { "maxAttempts": 2 },
                "circuitBreaker": { "threshold": 2, "cooldownMs": 60000 },
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/api/data");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.InRange(_upstream.ReceivedRequests.Count, 2, 3);
        Assert.Equal(10, healthy.ReceivedRequests.Count);
    }
}

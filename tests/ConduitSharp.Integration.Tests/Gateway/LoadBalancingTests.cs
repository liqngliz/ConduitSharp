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
        // Two independent upstream servers — round-robin must alternate between them.
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

        // Four requests — round-robin must visit each node exactly twice.
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
        // loadBalancingStrategy is a YARP policy name, not a closed enum — every policy YARP
        // registers is available without a schema change.
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
        // A typo (or a policy DLL that was never dropped in) must not sit dormant until the first
        // request picks a node. The gateway validates the name against the registered
        // ILoadBalancingPolicy set at startup, and the error names the route and what it could
        // have used — rather than leaving it to YARP's later, terser complaint.
        var routes = GatewayTestHelpers.CatchAllRoutes(_upstream.BaseUrl, lbStrategy: "RoundRobbin");
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/api/hello");
        });

        var message = ex.ToString();
        Assert.Contains("RoundRobbin", message, StringComparison.Ordinal);
        Assert.Contains("test-route", message, StringComparison.Ordinal);   // the offending route
        Assert.Contains("RoundRobin", message, StringComparison.Ordinal);   // ...and a valid one
    }

    [Fact]
    public async Task LoadBalancingPolicy_EnumRoundTripsToThePolicyName()
    {
        // The enum exists so C# callers get compile-time safety; YARP declares its policy names as
        // nameof(...), so ToString() is the wire value. If YARP ever renames one, this fails.
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
        // node1 always 503 (reachable but unhealthy — so we can count its hits);
        // node2 always 200. Circuit opens after 2 failures; retry masks the transition.
        // After it opens, node1 must stop being selected: it receives exactly `threshold`
        // requests no matter how many the client sends, while node2 serves the rest.
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
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // retry + failover keep it succeeding
        }

        // The unhealthy node is dropped after its circuit opens (2 failures), not hit
        // once per round for all 10 requests. Passive health state propagates just after
        // the failing response completes, so on a slow runner one extra request can pick
        // the dead node before the open circuit lands — allow that single race, no more.
        Assert.InRange(_upstream.ReceivedRequests.Count, 2, 3);
        Assert.Equal(10, healthy.ReceivedRequests.Count);
    }
}

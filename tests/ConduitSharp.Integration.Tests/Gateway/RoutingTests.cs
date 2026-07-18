using ConduitSharp.Integration.Tests.Fixtures;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Verifies route-matching behaviour: unmatched paths return 404 and
/// misconfigured routes (null upstream) return 502 without hitting the upstream.
/// </summary>
public sealed class RoutingTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    [Fact]
    public async Task UnmatchedPath_Returns404()
    {
        var routes = $$"""
            {
              "routes": [{
                "id": "narrow",
                "route": { "match": { "path": "/api/specific" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/other");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task HeaderConstraint_FiltersByHeaderPresence()
    {
        // match.headers is enforced by the native RouteConstraintMatcherPolicy — a request
        // missing the required header must not match the route (404, no forward), while the
        // same request carrying it forwards to the upstream.
        var routes = $$"""
            {
              "routes": [{
                "id": "internal-only",
                "route": { "match": { "path": "/api/thing", "headers": [ { "name": "X-Internal", "values": ["yes"], "mode": "ExactHeader" } ] } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var missing = await client.GetAsync("/api/thing");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);

        var withHeader = new HttpRequestMessage(HttpMethod.Get, "/api/thing");
        withHeader.Headers.Add("X-Internal", "yes");
        var matched = await client.SendAsync(withHeader);

        Assert.Equal(HttpStatusCode.OK, matched.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task QueryConstraint_MismatchedValue_Returns404()
    {
        var routes = $$"""
            {
              "routes": [{
                "id": "v2-only",
                "route": { "match": { "path": "/api/thing", "queryParameters": [ { "name": "version", "values": ["2"], "mode": "Exact" } ] } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/thing?version=1")).StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/thing?version=2")).StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task WrongMethodOnMatchingPath_Returns405()
    {
        // Right path, wrong verb is a 405 — endpoint routing's own answer. Regression: a catch-all
        // fallback endpoint would match every path and turn this into a 404.
        var routes = $$"""
            {
              "routes": [{
                "id": "read-only",
                "route": { "match": { "path": "/api/thing", "methods": ["GET"] } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/thing");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task NullUpstream_Returns502()
    {
        var routes = """
            {
              "routes": [{
                "id": "no-upstream",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": null,
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/anything");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }
}

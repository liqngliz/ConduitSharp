using ConduitSharp.Core.Routing;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Verifies the plugin pipeline mechanics: short-circuit stops upstream
/// forwarding, the status code and body are written to the response, and
/// any headers set during short-circuit are included.
/// </summary>
public sealed class PipelineTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    private string RoutesWithPlugin(string pluginName) => $$"""
        {
          "routes": [{
            "id": "test-route",
            "route": { "match": { "path": "/{**rest}" } },
            "cluster": {
              "loadBalancingPolicy": "RoundRobin",
              "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } },
              "httpRequest": { "activityTimeout": "00:00:05" }
            },
            "plugins": [{ "name": "{{pluginName}}", "order": 1, "enabled": true, "config": {} }]
          }]
        }
        """;

    [Fact]
    public async Task ShortCircuit_WritesStatusAndBody_DoesNotForwardToUpstream()
    {
        var plugin = new FixedStatusPlugin(PluginName.JwtAuth, 401, "Unauthorized");
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, RoutesWithPlugin("jwt-auth"), plugins: [plugin]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Unauthorized", await response.Content.ReadAsStringAsync());
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task ThrowingPlugin_Returns500_NotUnobservedException()
    {
        // A plugin that throws must surface as a clean 500 — not an unhandled
        // exception that exits the pipeline without a response — and must not
        // forward to the upstream.
        var plugin = new ThrowingPlugin(PluginName.JwtAuth);
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, RoutesWithPlugin("jwt-auth"), plugins: [plugin]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/protected");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
        Assert.DoesNotContain("boom from plugin", body); // exception detail not leaked
    }

    [Fact]
    public async Task ShortCircuit_WithHeaders_HeadersIncludedInResponse()
    {
        var plugin = new FixedStatusPlugin(
            PluginName.JwtAuth, 401, "Unauthorized",
            headers: new() { ["X-Short-Circuit"] = "true" });
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, RoutesWithPlugin("jwt-auth"), plugins: [plugin]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("true", response.Headers.GetValues("X-Short-Circuit").Single());
    }
}

using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// The gateway answers /healthz (liveness) and /readyz (readiness) itself — O1.
/// These must be independent of upstream reachability so a downstream blip does not
/// pull the gateway out of rotation.
/// </summary>
public sealed class HealthEndpointsTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    [Fact]
    public async Task Healthz_Returns200_EvenWhenUpstreamIsDown()
    {
        // Route forwards to a dead upstream — gateway liveness must not depend on it.
        var routes = GatewayTestHelpers.CatchAllRoutes(
            _upstream.BaseUrl, upstreamOverride: "http://127.0.0.1:1");
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readyz_Returns200_WhenRoutesLoaded()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ready", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readyz_Returns503_WhenNoRoutesLoaded()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, """{ "routes": [] }""");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}

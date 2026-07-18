using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Pipeline.Transformation;

[Trait("Contract", "PluginIsolation")]
public sealed class HeaderTransformEndToEndTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    [Fact]
    public async Task Same_plugin_on_four_routes_keeps_separate_configs()
    {
        // Four distinct transforms; the upstream must see exactly the transform of the
        // route that was hit — no set/add/remove bleeding across routes.
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "header-transform",
            new { set = new Dictionary<string, string> { ["X-From"] = "alpha" } },
            new { set = new Dictionary<string, string> { ["X-From"] = "beta" } },
            new
            {
                set = new Dictionary<string, string> { ["X-From"] = "gamma" },
                add = new Dictionary<string, string> { ["X-Extra"] = "c-only" },
            },
            new
            {
                set    = new Dictionary<string, string> { ["X-From"] = "delta" },
                remove = new[] { "X-Client" },
            });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var expectedFrom = new Dictionary<char, string>
        {
            ['a'] = "alpha", ['b'] = "beta", ['c'] = "gamma", ['d'] = "delta",
        };

        foreach (var route in "abcd")
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{route}/data");
            request.Headers.Add("X-Client", "from-client");

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var seen = _upstream.ReceivedRequests[^1].Headers;
            Assert.Equal(expectedFrom[route], seen["X-From"]);

            if (route == 'c') Assert.Equal("c-only", seen["X-Extra"]);
            else              Assert.False(seen.ContainsKey("X-Extra"),
                $"route /{route}: X-Extra from route c leaked");

            if (route == 'd') Assert.False(seen.ContainsKey("X-Client"),
                "route /d: X-Client should have been removed");
            else              Assert.Equal("from-client", seen["X-Client"]);
        }
    }
}

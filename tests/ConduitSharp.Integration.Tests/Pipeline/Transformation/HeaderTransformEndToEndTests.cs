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
    public async Task Same_plugin_on_four_routes_keeps_separate_request_configs()
    {
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "header-transform",
            new { request = new { set = new Dictionary<string, string> { ["X-From"] = "alpha" } } },
            new { request = new { set = new Dictionary<string, string> { ["X-From"] = "beta" } } },
            new
            {
                request = new
                {
                    set = new Dictionary<string, string> { ["X-From"] = "gamma" },
                    add = new Dictionary<string, string> { ["X-Extra"] = "c-only" },
                },
            },
            new
            {
                request = new
                {
                    set    = new Dictionary<string, string> { ["X-From"] = "delta" },
                    remove = new[] { "X-Client" },
                },
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

    [Fact]
    public async Task Response_block_strips_and_adds_headers_before_the_client_sees_them()
    {
        _upstream.RespondWith(async ctx =>
        {
            ctx.Response.Headers["X-Powered-By"] = "leaky-upstream";
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("ok");
        });

        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "header-transform",
            new
            {
                response = new
                {
                    remove = new[] { "X-Powered-By" },
                    set    = new Dictionary<string, string> { ["X-Frame-Options"] = "DENY" },
                },
            });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/a/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Powered-By"), "upstream X-Powered-By should be stripped");
        Assert.Equal("DENY", string.Join("", response.Headers.GetValues("X-Frame-Options")));
    }

    [Fact]
    public async Task Flat_config_shape_fails_at_startup()
    {
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "header-transform",
            new { set = new Dictionary<string, string> { ["X-From"] = "alpha" } });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/a/data");
        });
        Assert.Contains("flat shape", ex.ToString(), StringComparison.Ordinal);
    }
}

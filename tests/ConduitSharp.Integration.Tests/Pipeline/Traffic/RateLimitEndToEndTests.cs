using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Pipeline.Traffic;

public sealed class RateLimitEndToEndTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    [Fact]
    public async Task WithinLimit_ForwardsToUpstream()
    {
        var routes = GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "rate-limit",
            new { windowSeconds = 60, maxRequests = 5 });
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task OverLimit_Returns429WithRetryAfter()
    {
        // maxRequests=1: first call passes, second is blocked
        var routes = GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "rate-limit",
            new { windowSeconds = 60, maxRequests = 1 });
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        await client.GetAsync("/api");
        var response = await client.GetAsync("/api");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        // Retry-After reports the seconds remaining in the current fixed window,
        // not the full window length.
        var retryAfter = int.Parse(response.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retryAfter, 1, 60);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task Same_plugin_on_four_routes_keeps_separate_configs_and_counters()
    {
        // Distinct quotas per route; counters are keyed by route id, so exhausting one
        // route must neither consume nor widen another's quota.
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "rate-limit",
            new { windowSeconds = 60, maxRequests = 1 },
            new { windowSeconds = 60, maxRequests = 2 },
            new { windowSeconds = 60, maxRequests = 3 },
            new { windowSeconds = 60, maxRequests = 100 });

        var quota = new Dictionary<char, int> { ['a'] = 1, ['b'] = 2, ['c'] = 3, ['d'] = 100 };

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        foreach (var route in "abcd")
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var response = await client.GetAsync($"/{route}/api");

            var expected = attempt <= quota[route] ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests;
            Assert.True(expected == response.StatusCode,
                $"route /{route} attempt {attempt}: expected {expected}, got {response.StatusCode}");
        }
    }
}

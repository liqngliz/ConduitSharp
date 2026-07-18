using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Pipeline.Security;

public sealed class ApiKeyAuthEndToEndTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    private string Routes() =>
        GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "api-key-auth",
            new { header = "X-Api-Key", keys = new[] { "valid-key" } });

    // -------------------------------------------------------------------------
    // api-key-auth (plaintext)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NoKeyHeader_Returns401()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/data");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task WrongKey_Returns401()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync("/data");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task ValidKey_ForwardsToUpstream()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "valid-key");

        var response = await client.GetAsync("/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task Same_plugin_on_four_routes_keeps_separate_configs()
    {
        // Overlapping key lists across routes: a merge or overwrite of any route's
        // config breaks at least one cell of the 4x4 matrix below.
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "api-key-auth",
            new { header = "X-Api-Key", keys = new[] { "key-a" } },
            new { header = "X-Api-Key", keys = new[] { "key-b" } },
            new { header = "X-Api-Key", keys = new[] { "key-c", "key-a" } },
            new { header = "X-Api-Key", keys = new[] { "key-a", "key-b", "key-c" } });

        var accepts = new Dictionary<char, string[]>
        {
            ['a'] = ["key-a"],
            ['b'] = ["key-b"],
            ['c'] = ["key-c", "key-a"],
            ['d'] = ["key-a", "key-b", "key-c"],
        };

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        foreach (var route in "abcd")
        foreach (var key in new[] { "key-a", "key-b", "key-c", "key-d" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{route}/data");
            request.Headers.Add("X-Api-Key", key);

            var response = await client.SendAsync(request);

            var expected = accepts[route].Contains(key) ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
            Assert.True(expected == response.StatusCode,
                $"route /{route} with {key}: expected {expected}, got {response.StatusCode}");
        }
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task Hashed_same_plugin_on_four_routes_keeps_separate_configs()
    {
        // Same matrix as the plaintext variant, keys stored as SHA-256 hashes.
        string H(string raw) => PluginTestHelpers.Sha256Hex(raw);
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "api-key-auth-hashed",
            new { header = "X-Api-Key", keys = new[] { H("key-a") } },
            new { header = "X-Api-Key", keys = new[] { H("key-b") } },
            new { header = "X-Api-Key", keys = new[] { H("key-c"), H("key-a") } },
            new { header = "X-Api-Key", keys = new[] { H("key-a"), H("key-b"), H("key-c") } });

        var accepts = new Dictionary<char, string[]>
        {
            ['a'] = ["key-a"],
            ['b'] = ["key-b"],
            ['c'] = ["key-c", "key-a"],
            ['d'] = ["key-a", "key-b", "key-c"],
        };

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        foreach (var route in "abcd")
        foreach (var key in new[] { "key-a", "key-b", "key-c", "key-d" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{route}/data");
            request.Headers.Add("X-Api-Key", key);

            var response = await client.SendAsync(request);

            var expected = accepts[route].Contains(key) ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
            Assert.True(expected == response.StatusCode,
                $"route /{route} with {key}: expected {expected}, got {response.StatusCode}");
        }
    }

    // -------------------------------------------------------------------------
    // api-key-auth-hashed (SHA-256)
    // -------------------------------------------------------------------------

    private string HashedRoutes(string rawKey) =>
        GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "api-key-auth-hashed",
            new { header = "X-Api-Key", keys = new[] { PluginTestHelpers.Sha256Hex(rawKey) } });

    [Fact]
    public async Task Hashed_NoKeyHeader_Returns401()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, HashedRoutes("my-secret-key"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/data");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task Hashed_WrongKey_Returns401()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, HashedRoutes("my-secret-key"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "not-the-right-key");

        var response = await client.GetAsync("/data");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task Hashed_ValidRawKey_ForwardsToUpstream()
    {
        const string rawKey = "my-secret-key";
        await using var factory = await GatewayFactory.CreateAsync(_upstream, HashedRoutes(rawKey));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", rawKey);

        var response = await client.GetAsync("/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }
}

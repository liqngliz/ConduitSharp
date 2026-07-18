using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Pipeline.Security;

public sealed class JwtAuthEndToEndTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    private string Routes(string? key = null) =>
        GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "jwt-auth",
            new { signingKey = key ?? PluginTestHelpers.TestSecretBase64, algorithm = "HS256" });

    [Fact]
    public async Task NoAuthHeader_Returns401()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task MalformedToken_Returns401()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not.a.valid.jwt");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var token = PluginTestHelpers.BuildHs256Token(PluginTestHelpers.TestSecretBase64, expOffset: -3600);
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task SignedTokenWithNonIntegerExp_Returns401_Not500()
    {
        // Regression: exp.GetInt64() on a string exp threw InvalidOperationException,
        // which surfaced as a 500 from the middleware's catch-all instead of a 401.
        var token = PluginTestHelpers.BuildHs256Token(
            PluginTestHelpers.TestSecretBase64,
            extraClaims: new() { ["exp"] = "1700000000" });
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task WrongSigningKey_Returns401()
    {
        var wrongSecret = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("wrong-secret-key-here-wrong!!!!!"));
        var token = PluginTestHelpers.BuildHs256Token(wrongSecret, expOffset: +3600);
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task ValidToken_ForwardsToUpstream()
    {
        var token = PluginTestHelpers.BuildHs256Token(PluginTestHelpers.TestSecretBase64, expOffset: +3600);
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task Same_plugin_on_four_routes_keeps_separate_configs()
    {
        // 32-byte secrets (HS256 minimum), one per letter.
        string Secret(char c) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"integration-test-secret-key-{c}!!!"));

        // Routes a/b: single signingKey. Routes c/d: providers lists with overlapping
        // keys — a merge or overwrite of any route's config breaks a matrix cell.
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "jwt-auth",
            new { signingKey = Secret('a'), algorithm = "HS256" },
            new { signingKey = Secret('b'), algorithm = "HS256" },
            new { providers = new[]
            {
                new { signingKey = Secret('c'), algorithm = "HS256" },
                new { signingKey = Secret('a'), algorithm = "HS256" },
            }},
            new { providers = new[]
            {
                new { signingKey = Secret('a'), algorithm = "HS256" },
                new { signingKey = Secret('b'), algorithm = "HS256" },
                new { signingKey = Secret('c'), algorithm = "HS256" },
            }});

        var accepts = new Dictionary<char, char[]>
        {
            ['a'] = ['a'],
            ['b'] = ['b'],
            ['c'] = ['c', 'a'],
            ['d'] = ['a', 'b', 'c'],
        };

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        foreach (var route in "abcd")
        foreach (var signer in "abcd")
        {
            var token = PluginTestHelpers.BuildHs256Token(Secret(signer), expOffset: +3600);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{route}/protected");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await client.SendAsync(request);

            var expected = accepts[route].Contains(signer) ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
            Assert.True(expected == response.StatusCode,
                $"route /{route} with token signed by key {signer}: expected {expected}, got {response.StatusCode}");
        }
    }

    // -------------------------------------------------------------------------
    // requiredClaims (RBAC) — a valid token lacking permission is 403, not 401
    // -------------------------------------------------------------------------

    private string RoutesWithRequiredRole() =>
        GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "jwt-auth", new
        {
            signingKey = PluginTestHelpers.TestSecretBase64,
            algorithm  = "HS256",
            requiredClaims = new[] { new { claim = "roles", anyOf = new[] { "Finance.Admin" } } }
        });

    [Fact]
    public async Task ValidTokenMissingRequiredRole_Returns403()
    {
        var token = PluginTestHelpers.BuildHs256Token(
            PluginTestHelpers.TestSecretBase64,
            expOffset: +3600,
            extraClaims: new() { ["roles"] = new[] { "Other" } });
        await using var factory = await GatewayFactory.CreateAsync(_upstream, RoutesWithRequiredRole());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task ValidTokenWithRequiredRole_ForwardsToUpstream()
    {
        var token = PluginTestHelpers.BuildHs256Token(
            PluginTestHelpers.TestSecretBase64,
            expOffset: +3600,
            extraClaims: new() { ["roles"] = new[] { "Finance.Admin" } });
        await using var factory = await GatewayFactory.CreateAsync(_upstream, RoutesWithRequiredRole());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task MalformedRequiredClaimsConfig_FailsAtStartup()
    {
        var routes = GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "jwt-auth", new
        {
            signingKey     = PluginTestHelpers.TestSecretBase64,
            requiredClaims = new[] { new { claim = "" } }
        });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
            using var client = factory.CreateClient();
            await client.GetAsync("/protected");
        });
    }
}

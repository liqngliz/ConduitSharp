using Microsoft.IdentityModel.Tokens;
using Xunit;
using ConduitSharp.Security.Jwt;
using ConduitSharp.Security.Tests.Helpers;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class JwksJwtAuthPluginTests
{
    private static readonly JwksJwtAuthConfig DefaultConfig = new()
    {
        JwksUri = "https://stub.example.com/.well-known/jwks.json"
    };

    /// <summary>Real handler wired to a canned key provider serving the test RSA key.</summary>
    private static JwksJwtAuthHandler RsaHandler() =>
        new(new StubFactory(new StubConfigurationManager(AsymmetricTokenKit.RsaJwk())));

    private static JwksJwtAuthPlugin Plugin(JwksJwtAuthHandler? handler = null) =>
        new(handler ?? RsaHandler());

    private static System.Text.Json.JsonElement Configured(JwksJwtAuthConfig? config = null) =>
        System.Text.Json.JsonSerializer.SerializeToElement(config ?? DefaultConfig);

    private static string ValidBearer(string payloadJson = """{"sub":"test"}""") =>
        $"Bearer {AsymmetricTokenKit.SignRs256(payloadJson)}";

    // -------------------------------------------------------------------------
    // Bearer extraction failures
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateConfig_MissingJwksUri_Throws()
    {
        var plugin = Plugin();
        var config = System.Text.Json.JsonSerializer.SerializeToElement(new { });

        var ex = Assert.Throws<InvalidOperationException>(() => plugin.ValidateConfig(config));
        Assert.Contains("jwksUri", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Multiple Providers
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MultipleProviders_SucceedsIfAnyProviderValidates()
    {
        // Provider 1 fails signature, Provider 2 succeeds
        var config = new JwksJwtAuthConfig
        {
            Providers =
            [
                new JwksProviderConfig { JwksUri = "https://wrong.example.com", Issuer = "wrong" },
                new JwksProviderConfig { JwksUri = "https://stub.example.com/.well-known/jwks.json" }
            ]
        };

        var plugin            = Plugin();
        var context           = HttpContextBuilder.WithAuth(ValidBearer());
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await plugin.ExecuteAsync(context, Configured(config), next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }

    [Fact]
    public async Task ExecuteAsync_MultipleProviders_Returns403IfAnyProviderFailsRbac()
    {
        // Provider 1 matches signature but fails RBAC (should yield 403)
        // Provider 2 fails signature (should yield 401)
        // Expected final result: 403 (Forbidden is higher precedence than Unauthorized)
        var config = new JwksJwtAuthConfig
        {
            Providers =
            [
                new JwksProviderConfig 
                { 
                    JwksUri = "https://stub.example.com/.well-known/jwks.json",
                    RequiredClaims = [ new RequiredClaim { Claim = "roles", AnyOf = ["Admin"] } ] // Token doesn't have this
                },
                new JwksProviderConfig 
                { 
                    JwksUri = "https://wrong.example.com", Issuer = "wrong"
                }
            ]
        };

        var plugin  = Plugin();
        var context = HttpContextBuilder.WithAuth(ValidBearer());

        await plugin.ExecuteAsync(context, Configured(config), HttpContextBuilder.NoOpNext());

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_NoAuthHeader_ShortCircuits401()
    {
        var plugin  = Plugin();
        var context = HttpContextBuilder.NoHeaders();

        await plugin.ExecuteAsync(context, Configured(), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_NonBearerScheme_ShortCircuits401()
    {
        var plugin  = Plugin();
        var context = HttpContextBuilder.WithAuth("Basic dXNlcjpwYXNz");

        await plugin.ExecuteAsync(context, Configured(), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Handler validation failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TokenSignedByWrongKey_ShortCircuits401()
    {
        var plugin  = Plugin();
        var context = HttpContextBuilder.WithAuth(
            $"Bearer {AsymmetricTokenKit.SignRs256WithWrongKey("""{"sub":"test"}""")}");

        await plugin.ExecuteAsync(context, Configured(), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_KeyNotFound_ShortCircuits401()
    {
        var noKeyHandler = new JwksJwtAuthHandler(
            new StubFactory(new StubConfigurationManager(null)));

        var plugin  = Plugin(handler: noKeyHandler);
        var context = HttpContextBuilder.WithAuth(ValidBearer());

        await plugin.ExecuteAsync(context, Configured(), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ValidToken_CallsNext()
    {
        var plugin            = Plugin();
        var context           = HttpContextBuilder.WithAuth(ValidBearer());
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await plugin.ExecuteAsync(context, Configured(), next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }

    // -------------------------------------------------------------------------
    // requiredClaims (RBAC) — 403, not 401, on a valid token lacking permission
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ValidTokenMissingRequiredRole_ShortCircuits403()
    {
        var config = DefaultConfig with
        {
            RequiredClaims = [new RequiredClaim { Claim = "roles", AnyOf = ["Finance.Admin"] }]
        };
        var plugin  = new JwksJwtAuthPlugin(RsaHandler());
        var context = HttpContextBuilder.WithAuth(ValidBearer("""{"roles":["Other"]}"""));

        await plugin.ExecuteAsync(context, Configured(config), HttpContextBuilder.NoOpNext());

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_ValidTokenWithRequiredRole_CallsNext()
    {
        var config = DefaultConfig with
        {
            RequiredClaims = [new RequiredClaim { Claim = "roles", AnyOf = ["Finance.Admin"] }]
        };
        var plugin            = new JwksJwtAuthPlugin(RsaHandler());
        var context           = HttpContextBuilder.WithAuth(ValidBearer("""{"roles":["Finance.Admin"]}"""));
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await plugin.ExecuteAsync(context, Configured(config), next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidTokenWithRequiredClaims_StillReturns401NotChecked()
    {
        // A failed handler validation must short-circuit 401 before requiredClaims is
        // ever evaluated — requiredClaims is present here but never reached.
        var config = DefaultConfig with
        {
            RequiredClaims = [new RequiredClaim { Claim = "roles", AnyOf = ["Admin"] }]
        };
        var plugin  = new JwksJwtAuthPlugin(RsaHandler());
        var context = HttpContextBuilder.WithAuth(
            $"Bearer {AsymmetricTokenKit.SignRs256WithWrongKey("""{"roles":["Admin"]}""")}");

        await plugin.ExecuteAsync(context, Configured(config), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Config comes from context.PluginConfig (routes.json round-trip)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ReadsConfigFromContextPluginConfig()
    {
        // The audience constraint only exists in the JSON placed on the context — proving
        // ExecuteAsync parses context.PluginConfig rather than any ambient default.
        var plugin  = Plugin();
        var context = HttpContextBuilder.WithAuth(ValidBearer());
        var config = Configured(DefaultConfig with { Audience = "some-other-api" });

        await plugin.ExecuteAsync(context, config, HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Per-route config isolation: one plugin/handler/factory (the singleton wiring),
    // four route configs with overlapping jwksUris — keys must stay per URI.
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task ExecuteAsync_SamePluginFourConfigs_KeysStayPerJwksUri()
    {
        static string Uri(char c) => $"https://idp-{c}.example.com/jwks";

        var kits = "abcd".ToDictionary(c => c, _ => new AsymmetricTokenKit.RsaKit());
        var factory = new PerUriStubFactory("abc".ToDictionary(Uri,
            c => (Microsoft.IdentityModel.Protocols.IConfigurationManager<JsonWebKeySet>)
                new StubConfigurationManager(kits[c].Jwk())));
        var plugin = new JwksJwtAuthPlugin(new JwksJwtAuthHandler(factory));

        var configs = new Dictionary<char, JwksJwtAuthConfig>
        {
            ['a'] = new() { JwksUri = Uri('a') },
            ['b'] = new() { JwksUri = Uri('b') },
            ['c'] = new() { Providers = [new() { JwksUri = Uri('c') }, new() { JwksUri = Uri('a') }] },
            ['d'] = new() { Providers = [new() { JwksUri = Uri('a') }, new() { JwksUri = Uri('b') }, new() { JwksUri = Uri('c') }] },
        };
        var accepts = new Dictionary<char, char[]>
        {
            ['a'] = ['a'],
            ['b'] = ['b'],
            ['c'] = ['c', 'a'],
            ['d'] = ['a', 'b', 'c'],  // key d valid nowhere
        };

        foreach (var route in "abcd")
        foreach (var signer in "abcd")
        {
            var context           = HttpContextBuilder.WithAuth($"Bearer {kits[signer].SignRs256("""{"sub":"test"}""")}");
            var (next, wasCalled) = HttpContextBuilder.TrackingNext();

            await plugin.ExecuteAsync(context, Configured(configs[route]), next);

            var expectOk = accepts[route].Contains(signer);
            Assert.True(expectOk == wasCalled() && (expectOk ? 200 : 401) == context.Response.StatusCode,
                $"route {route} with token signed by key {signer}: expected {(expectOk ? 200 : 401)}, " +
                $"got {context.Response.StatusCode} (next called: {wasCalled()})");
        }
    }
}

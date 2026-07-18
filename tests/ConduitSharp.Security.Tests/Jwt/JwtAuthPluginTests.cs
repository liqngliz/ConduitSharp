using Xunit;
using ConduitSharp.Security.Jwt;
using ConduitSharp.Security.Tests.Helpers;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class JwtAuthPluginTests
{
    private static JwtAuthConfig DefaultConfig => new()
    {
        SigningKey = JwtTokenBuilder.DefaultSecretBase64,
        Algorithm  = "HS256"
    };

    private static readonly JwtAuthPlugin Plugin = new(new JwtAuthHandler());

    private static System.Text.Json.JsonElement Configured(JwtAuthConfig? config = null) =>
        System.Text.Json.JsonSerializer.SerializeToElement(config ?? DefaultConfig);

    // -------------------------------------------------------------------------
    // Bearer extraction failures — no handler involvement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoAuthHeader_ShortCircuits401()
    {
        var context = HttpContextBuilder.NoHeaders();

        await Plugin.ExecuteAsync(context, Configured(), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_NonBearerScheme_ShortCircuits401()
    {
        var context = HttpContextBuilder.WithAuth("Basic dXNlcjpwYXNz");

        await Plugin.ExecuteAsync(context, Configured(), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Token validation failures
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MalformedToken_ShortCircuits401()
    {
        var context = HttpContextBuilder.WithAuth("Bearer not.a.jwt");

        await Plugin.ExecuteAsync(context, Configured(), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredToken_ShortCircuits401()
    {
        var context = HttpContextBuilder.WithAuth($"Bearer {JwtTokenBuilder.ExpiredToken()}");

        await Plugin.ExecuteAsync(context, Configured(), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_BadSignature_ShortCircuits401()
    {
        var context = HttpContextBuilder.WithAuth($"Bearer {JwtTokenBuilder.BadSignatureToken()}");

        await Plugin.ExecuteAsync(context, Configured(), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WrongIssuer_ShortCircuits401()
    {
        var config  = DefaultConfig with { Issuer = "https://expected.example.com" };
        var token   = JwtTokenBuilder.ValidToken(issuer: "https://other.example.com");
        var context = HttpContextBuilder.WithAuth($"Bearer {token}");

        await Plugin.ExecuteAsync(context, Configured(config), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WrongAudience_ShortCircuits401()
    {
        var config  = DefaultConfig with { Audience = "expected-api" };
        var token   = JwtTokenBuilder.ValidToken(audience: "other-api");
        var context = HttpContextBuilder.WithAuth($"Bearer {token}");

        await Plugin.ExecuteAsync(context, Configured(config), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateConfig_MissingSigningKey_Throws()
    {
        var plugin = new JwtAuthPlugin(new JwtAuthHandler());
        var config = System.Text.Json.JsonSerializer.SerializeToElement(new { });

        var ex = Assert.Throws<InvalidOperationException>(() => plugin.ValidateConfig(config));
        Assert.Contains("signingKey", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Multiple Providers
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MultipleProviders_SucceedsIfAnyProviderValidates()
    {
        var config = new JwtAuthConfig
        {
            Providers =
            [
                new JwtProviderConfig { SigningKey = JwtTokenBuilder.DefaultSecretBase64, Issuer = "wrong" },
                new JwtProviderConfig { SigningKey = JwtTokenBuilder.DefaultSecretBase64 }
            ]
        };

        var plugin            = new JwtAuthPlugin(new JwtAuthHandler());
        var context           = HttpContextBuilder.WithAuth($"Bearer {JwtTokenBuilder.ValidToken()}");
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await plugin.ExecuteAsync(context, Configured(config), next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }

    [Fact]
    public async Task ExecuteAsync_MultipleProviders_Returns403IfAnyProviderFailsRbac()
    {
        var config = new JwtAuthConfig
        {
            Providers =
            [
                new JwtProviderConfig 
                { 
                    SigningKey = JwtTokenBuilder.DefaultSecretBase64,
                    RequiredClaims = [ new RequiredClaim { Claim = "roles", AnyOf = ["Admin"] } ]
                },
                new JwtProviderConfig 
                { 
                    SigningKey = Convert.ToBase64String(new byte[32])
                }
            ]
        };

        var plugin  = new JwtAuthPlugin(new JwtAuthHandler());
        var context = HttpContextBuilder.WithAuth($"Bearer {JwtTokenBuilder.ValidToken()}");

        await plugin.ExecuteAsync(context, Configured(config), HttpContextBuilder.NoOpNext());

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_ValidToken_CallsNext()
    {
        var token             = JwtTokenBuilder.ValidToken();
        var context = HttpContextBuilder.WithAuth($"Bearer {token}");
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await Plugin.ExecuteAsync(context, Configured(), next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }

    [Fact]
    public async Task ExecuteAsync_ValidTokenWithIssuerAndAudience_CallsNext()
    {
        var config  = DefaultConfig with
        {
            Issuer   = "https://auth.example.com",
            Audience = "my-api"
        };
        var token             = JwtTokenBuilder.ValidToken(issuer: "https://auth.example.com", audience: "my-api");
        var context = HttpContextBuilder.WithAuth($"Bearer {token}");
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await Plugin.ExecuteAsync(context, Configured(config), next);

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
        var token   = JwtTokenBuilder.ValidToken(extraClaims: new() { ["roles"] = new[] { "Other" } });
        var context = HttpContextBuilder.WithAuth($"Bearer {token}");

        await Plugin.ExecuteAsync(context, Configured(config), HttpContextBuilder.NoOpNext());

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_ValidTokenWithRequiredRole_CallsNext()
    {
        var config = DefaultConfig with
        {
            RequiredClaims = [new RequiredClaim { Claim = "roles", AnyOf = ["Finance.Admin"] }]
        };
        var token             = JwtTokenBuilder.ValidToken(extraClaims: new() { ["roles"] = new[] { "Finance.Admin" } });
        var context = HttpContextBuilder.WithAuth($"Bearer {token}");
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await Plugin.ExecuteAsync(context, Configured(config), next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidSignatureWithRequiredClaims_StillReturns401NotChecked()
    {
        // A bad signature must short-circuit 401 before requiredClaims is ever evaluated.
        var config = DefaultConfig with
        {
            RequiredClaims = [new RequiredClaim { Claim = "roles", AnyOf = ["Admin"] }]
        };
        var context = HttpContextBuilder.WithAuth($"Bearer {JwtTokenBuilder.BadSignatureToken()}");

        await Plugin.ExecuteAsync(context, Configured(config), HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // ValidateConfig — fails fast on a malformed requiredClaims block
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateConfig_MalformedRequiredClaims_Throws()
    {
        // Exercises the fail-fast path a route reload would hit.
        var plugin = new JwtAuthPlugin(new JwtAuthHandler());
        var config = System.Text.Json.JsonDocument.Parse("""
            { "signingKey": "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo", "requiredClaims": [ { "claim": "" } ] }
            """).RootElement;

        Assert.Throws<InvalidOperationException>(() => plugin.ValidateConfig(config));
    }

    [Fact]
    public void ValidateConfig_ValidRequiredClaims_DoesNotThrow()
    {
        var plugin = new JwtAuthPlugin(new JwtAuthHandler());
        var config = System.Text.Json.JsonDocument.Parse("""
            { "signingKey": "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo", "requiredClaims": [ { "claim": "roles", "anyOf": ["Admin"] } ] }
            """).RootElement;

        plugin.ValidateConfig(config);
    }

    // -------------------------------------------------------------------------
    // Config comes from context.PluginConfig (routes.json round-trip)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ReadsConfigFromContextPluginConfig()
    {
        // The issuer constraint only exists in the JSON placed on the context — proving
        // ExecuteAsync parses context.PluginConfig rather than any ambient default.
        var token   = JwtTokenBuilder.ValidToken(issuer: "https://other.example.com");
        var context = HttpContextBuilder.WithAuth($"Bearer {token}");
        var config = Configured(DefaultConfig with { Issuer = "https://auth.example.com" });

        await Plugin.ExecuteAsync(context, config, HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }
}

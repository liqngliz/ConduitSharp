using System.Text.Json;
using Xunit;
using ConduitSharp.Security.Jwt;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class JwtAuthConfigTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    // base64("demo-signing-key-conduitsharp-example-32ch") — 43 raw bytes, over the
    // 32-byte HS256 minimum enforced at load time.
    private const string ValidKey = "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo";

    // -------------------------------------------------------------------------
    // Deserialisation
    // -------------------------------------------------------------------------

    [Fact]
    public void From_FullConfig_BindsAllFields()
    {
        var config = JwtAuthConfig.From(Json($$"""
            {
                "signingKey": "{{ValidKey}}",
                "algorithm":  "HS256",
                "issuer":     "https://auth.example.com",
                "audience":   "my-api"
            }
            """));

        Assert.Equal(ValidKey, config.SigningKey);
        Assert.Equal("HS256",  config.Algorithm);
        Assert.Equal("https://auth.example.com", config.Issuer);
        Assert.Equal("my-api", config.Audience);
    }

    [Fact]
    public void From_CaseInsensitiveKeys_BindsCorrectly()
    {
        var config = JwtAuthConfig.From(Json($$"""
            { "SIGNINGKEY": "{{ValidKey}}", "ALGORITHM": "HS256" }
            """));

        Assert.Equal(ValidKey, config.SigningKey);
        Assert.Equal("HS256",  config.Algorithm);
    }

    [Fact]
    public void From_OnlySigningKey_UsesDefaults()
    {
        var config = JwtAuthConfig.From(Json($$"""{ "signingKey": "{{ValidKey}}" }"""));

        Assert.Equal(ValidKey, config.SigningKey);
        Assert.Equal("HS256",  config.Algorithm);  // default
        Assert.Null(config.Issuer);                // optional, absent → null
        Assert.Null(config.Audience);              // optional, absent → null
    }

    // -------------------------------------------------------------------------
    // signingKey validation — fails at load time, not on the first request
    // -------------------------------------------------------------------------

    [Fact]
    public void From_MissingSigningKey_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtAuthConfig.From(Json("{}")));
        Assert.Contains("signingKey", ex.Message);
    }

    [Fact]
    public void From_RawPassphraseSigningKey_ThrowsWithBase64Hint()
    {
        // The classic interop landmine: a raw secret ("your-256-bit-secret") pasted in
        // instead of its base64 encoding used to reject every token at runtime with no hint.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtAuthConfig.From(Json("""{ "signingKey": "your-256-bit-secret" }""")));
        Assert.Contains("base64", ex.Message);
    }

    [Fact]
    public void From_SigningKeyUnder32Bytes_Throws()
    {
        // "dGVzdC1rZXk=" = base64("test-key") — valid base64 but only 8 bytes.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtAuthConfig.From(Json("""{ "signingKey": "dGVzdC1rZXk=" }""")));
        Assert.Contains("32 bytes", ex.Message);
    }

    // -------------------------------------------------------------------------
    // requiredClaims (RBAC)
    // -------------------------------------------------------------------------

    [Fact]
    public void From_NoRequiredClaims_IsNull()
    {
        var config = JwtAuthConfig.From(Json($$"""{ "signingKey": "{{ValidKey}}" }"""));
        Assert.Null(config.RequiredClaims);
    }

    [Fact]
    public void From_RequiredClaims_BindsRules()
    {
        var config = JwtAuthConfig.From(Json($$"""
            {
                "signingKey": "{{ValidKey}}",
                "requiredClaims": [
                    { "claim": "roles", "anyOf": ["Admin"] },
                    { "claim": "hd" }
                ]
            }
            """));

        Assert.NotNull(config.RequiredClaims);
        Assert.Equal(2, config.RequiredClaims!.Count);
        Assert.Equal("roles", config.RequiredClaims[0].Claim);
        Assert.Equal(["Admin"], config.RequiredClaims[0].AnyOf);
        Assert.Equal("hd", config.RequiredClaims[1].Claim);
    }

    [Fact]
    public void From_RequiredClaimsWithEmptyClaimName_ThrowsAtLoadTime()
    {
        Assert.Throws<InvalidOperationException>(() => JwtAuthConfig.From(Json($$"""
            { "signingKey": "{{ValidKey}}", "requiredClaims": [ { "claim": "" } ] }
            """)));
    }

    [Fact]
    public void From_RequiredClaimsWithTwoMatchers_ThrowsAtLoadTime()
    {
        Assert.Throws<InvalidOperationException>(() => JwtAuthConfig.From(Json($$"""
            { "signingKey": "{{ValidKey}}", "requiredClaims": [ { "claim": "roles", "equals": "a", "anyOf": ["a"] } ] }
            """)));
    }

    // -------------------------------------------------------------------------
    // Null / invalid input
    // -------------------------------------------------------------------------

    [Fact]
    public void From_NullJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => JwtAuthConfig.From(Json("null")));
    }
}

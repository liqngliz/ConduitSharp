using System.Text.Json;
using Xunit;
using ConduitSharp.Security.Jwt;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class JwtAuthConfigTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private const string ValidKey = "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo";

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
        Assert.Equal("HS256",  config.Algorithm);
        Assert.Null(config.Issuer);
        Assert.Null(config.Audience);
    }

    [Fact]
    public void From_MissingSigningKey_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtAuthConfig.From(Json("{}")));
        Assert.Contains("signingKey", ex.Message);
    }

    [Fact]
    public void From_RawPassphraseSigningKey_ThrowsWithBase64Hint()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtAuthConfig.From(Json("""{ "signingKey": "your-256-bit-secret" }""")));
        Assert.Contains("base64", ex.Message);
    }

    [Fact]
    public void From_SigningKeyUnder32Bytes_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtAuthConfig.From(Json("""{ "signingKey": "dGVzdC1rZXk=" }""")));
        Assert.Contains("32 bytes", ex.Message);
    }

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

    [Fact]
    public void From_NullJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => JwtAuthConfig.From(Json("null")));
    }
}

using System.Text.Json;
using Xunit;
using ConduitSharp.Security.Jwt;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class JwksJwtAuthConfigTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    // -------------------------------------------------------------------------
    // Deserialisation
    // -------------------------------------------------------------------------

    [Fact]
    public void From_FullConfig_BindsAllFields()
    {
        var config = JwksJwtAuthConfig.From(Json("""
            {
                "jwksUri":         "https://auth.example.com/.well-known/jwks.json",
                "issuer":          "https://auth.example.com/",
                "audience":        "my-api",
                "cacheTtlSeconds": 7200
            }
            """));

        Assert.Equal("https://auth.example.com/.well-known/jwks.json", config.JwksUri);
        Assert.Equal("https://auth.example.com/", config.Issuer);
        Assert.Equal("my-api", config.Audience);
        Assert.Equal(7200,     config.CacheTtlSeconds);
    }

    [Fact]
    public void From_CaseInsensitiveKeys_BindsCorrectly()
    {
        var config = JwksJwtAuthConfig.From(Json("""
            { "JWKSURI": "https://example.com/jwks", "CACHETLSECONDS": 60 }
            """));

        Assert.Equal("https://example.com/jwks", config.JwksUri);
    }

    [Fact]
    public void From_OnlyJwksUri_UsesDefaults()
    {
        var config = JwksJwtAuthConfig.From(Json("""
            { "jwksUri": "https://auth.example.com/.well-known/jwks.json" }
            """));

        Assert.Equal(3600, config.CacheTtlSeconds);  // default
        Assert.Equal(5000, config.JwksTimeoutMs);     // default
        Assert.Null(config.Issuer);                   // optional
        Assert.Null(config.Audience);                 // optional
    }

    // -------------------------------------------------------------------------
    // jwksUri validation — required field
    // -------------------------------------------------------------------------

    [Fact]
    public void From_MissingJwksUri_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => JwksJwtAuthConfig.From(Json("""{ "issuer": "https://auth.example.com/" }""")));

        Assert.Contains("jwksUri", ex.Message);
    }

    [Fact]
    public void From_EmptyJwksUri_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => JwksJwtAuthConfig.From(Json("""{ "jwksUri": "" }""")));

        Assert.Contains("jwksUri", ex.Message);
    }

    [Fact]
    public void From_WhitespaceJwksUri_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => JwksJwtAuthConfig.From(Json("""{ "jwksUri": "   " }""")));

        Assert.Contains("jwksUri", ex.Message);
    }

    // -------------------------------------------------------------------------
    // requiredClaims (RBAC)
    // -------------------------------------------------------------------------

    [Fact]
    public void From_RequiredClaims_BindsRules()
    {
        var config = JwksJwtAuthConfig.From(Json("""
            {
                "jwksUri": "https://auth.example.com/.well-known/jwks.json",
                "requiredClaims": [ { "claim": "scp", "allOf": ["reports.read"], "delimiter": " " } ]
            }
            """));

        Assert.NotNull(config.RequiredClaims);
        Assert.Equal("scp", config.RequiredClaims![0].Claim);
        Assert.Equal(["reports.read"], config.RequiredClaims[0].AllOf);
        Assert.Equal(" ", config.RequiredClaims[0].Delimiter);
    }

    [Fact]
    public void From_RequiredClaimsWithEmptyAnyOf_ThrowsAtLoadTime()
    {
        Assert.Throws<InvalidOperationException>(() => JwksJwtAuthConfig.From(Json("""
            {
                "jwksUri": "https://auth.example.com/.well-known/jwks.json",
                "requiredClaims": [ { "claim": "roles", "anyOf": [] } ]
            }
            """)));
    }

    // -------------------------------------------------------------------------
    // Null / invalid input
    // -------------------------------------------------------------------------

    [Fact]
    public void From_NullJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => JwksJwtAuthConfig.From(Json("null")));
    }

    [Fact]
    public void From_EmptyObject_ThrowsOnMissingJwksUri()
    {
        Assert.Throws<InvalidOperationException>(
            () => JwksJwtAuthConfig.From(Json("{}")));
    }
}

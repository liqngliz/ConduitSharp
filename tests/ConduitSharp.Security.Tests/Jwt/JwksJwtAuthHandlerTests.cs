using Microsoft.IdentityModel.Tokens;
using Xunit;
using ConduitSharp.Security.Jwt;
using ConduitSharp.Security.Tests.Helpers;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class JwksJwtAuthHandlerTests
{
    // -------------------------------------------------------------------------
    // Helpers — real RSA/EC keys, canned key provider (no HTTP)
    // -------------------------------------------------------------------------

    private static JwksProviderConfig Config(string? issuer = null, string? audience = null) =>
        new() { JwksUri = "https://stub.example.com/.well-known/jwks.json", Issuer = issuer, Audience = audience };

    private static JwksJwtAuthHandler Handler(JsonWebKey? key = null, Exception? throwEx = null) =>
        new(new StubFactory(new StubConfigurationManager(key, throwEx)));

    private static JwksJwtAuthHandler RsaHandler() => Handler(AsymmetricTokenKit.RsaJwk());

    private static string Payload(
        string? issuer = null, string? audience = null, long? exp = null, long? nbf = null)
    {
        var claims = new Dictionary<string, object?> { ["sub"] = "u1" };
        if (issuer   is not null) claims["iss"] = issuer;
        if (audience is not null) claims["aud"] = audience;
        if (exp      is not null) claims["exp"] = exp;
        if (nbf      is not null) claims["nbf"] = nbf;
        return System.Text.Json.JsonSerializer.Serialize(claims);
    }

    // -------------------------------------------------------------------------
    // Structure checks
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("header.payload")]
    [InlineData("a.b.c.d")]
    [InlineData("onlyone")]
    [InlineData("!!!.payload.sig")]
    public async Task Malformed_ReturnsFalse(string token)
    {
        var (ok, err, _) = await RsaHandler().TryValidateAsync(token, Config());

        Assert.False(ok);
        Assert.Contains("Malformed", err);
    }

    // -------------------------------------------------------------------------
    // Algorithm checks — symmetric and unsigned algs are rejected up front
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("HS256")]
    [InlineData("none")]
    public async Task UnsupportedAlgorithm_ReturnsFalse(string alg)
    {
        var (ok, err, _) = await RsaHandler()
            .TryValidateAsync(AsymmetricTokenKit.UnsignedToken(alg), Config());

        Assert.False(ok);
        Assert.Contains("Unsupported algorithm", err);
    }

    // -------------------------------------------------------------------------
    // Key lookup failures
    // -------------------------------------------------------------------------

    [Fact]
    public async Task KeyProviderReturnsNull_WithKid_ReturnsKidError()
    {
        var (ok, err, _) = await Handler(key: null)
            .TryValidateAsync(AsymmetricTokenKit.SignRs256(Payload()), Config());

        Assert.False(ok);
        Assert.Contains("No signing keys found in JWKS.", err);
    }

    [Fact]
    public async Task KeyProviderReturnsNull_NoKid_ReturnsGenericError()
    {
        var (ok, err, _) = await Handler(key: null)
            .TryValidateAsync(AsymmetricTokenKit.SignRs256(Payload(), kid: null), Config());

        Assert.False(ok);
        Assert.Contains("No signing keys found in JWKS.", err);
    }

    [Fact]
    public async Task KeyProviderThrows_ReturnsFetchError()
    {
        var handler = Handler(throwEx: new TimeoutException("JWKS fetch exceeded 5000 ms."));

        var (ok, err, _) = await handler
            .TryValidateAsync(AsymmetricTokenKit.SignRs256(Payload()), Config());

        Assert.False(ok);
        Assert.Contains("Failed to fetch JWKS", err);
    }

    // -------------------------------------------------------------------------
    // Signature verification — real crypto, both key types
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidRs256Token_ReturnsTrue_AndExposesClaims()
    {
        var (ok, err, claims) = await RsaHandler()
            .TryValidateAsync(AsymmetricTokenKit.SignRs256(Payload()), Config());

        Assert.True(ok, err);
        Assert.Null(err);
        Assert.Equal("u1", claims.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task ValidEs256Token_ReturnsTrue()
    {
        var (ok, err, _) = await Handler(AsymmetricTokenKit.EcJwk())
            .TryValidateAsync(AsymmetricTokenKit.SignEs256(Payload()), Config());

        Assert.True(ok);
        Assert.Null(err);
    }

    [Fact]
    public async Task TokenSignedByWrongKey_ReturnsFalse()
    {
        var (ok, err, _) = await RsaHandler()
            .TryValidateAsync(AsymmetricTokenKit.SignRs256WithWrongKey(Payload()), Config());

        Assert.False(ok);
        Assert.Contains("signature", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TamperedPayload_ReturnsFalse()
    {
        var token    = AsymmetricTokenKit.SignRs256(Payload());
        var parts    = token.Split('.');
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode("""{"sub":"admin"}"""u8.ToArray())}.{parts[2]}";

        var (ok, _, _) = await RsaHandler().TryValidateAsync(tampered, Config());

        Assert.False(ok);
    }

    // -------------------------------------------------------------------------
    // Claims — lifetime, issuer, audience (validated by the library)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExpiredToken_ReturnsFalse()
    {
        var expired = Payload(exp: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());

        var (ok, err, _) = await RsaHandler()
            .TryValidateAsync(AsymmetricTokenKit.SignRs256(expired), Config());

        Assert.False(ok);
        Assert.Equal("Token has expired.", err);
    }

    [Fact]
    public async Task NotYetValidToken_ReturnsFalse()
    {
        var future = Payload(nbf: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());

        var (ok, err, _) = await RsaHandler()
            .TryValidateAsync(AsymmetricTokenKit.SignRs256(future), Config());

        Assert.False(ok);
        Assert.Equal("Token not yet valid.", err);
    }

    [Fact]
    public async Task WrongIssuer_ReturnsFalse()
    {
        var (ok, err, _) = await RsaHandler().TryValidateAsync(
            AsymmetricTokenKit.SignRs256(Payload(issuer: "https://other.example.com")),
            Config(issuer: "https://auth.example.com"));

        Assert.False(ok);
        Assert.Equal("Invalid issuer.", err);
    }

    [Fact]
    public async Task WrongAudience_ReturnsFalse()
    {
        var (ok, err, _) = await RsaHandler().TryValidateAsync(
            AsymmetricTokenKit.SignRs256(Payload(audience: "other-api")),
            Config(audience: "my-api"));

        Assert.False(ok);
        Assert.Equal("Invalid audience.", err);
    }

    [Fact]
    public async Task MatchingIssuerAndAudience_ReturnsTrue()
    {
        var payload = Payload(issuer: "https://auth.example.com", audience: "my-api");

        var (ok, _, _) = await RsaHandler().TryValidateAsync(
            AsymmetricTokenKit.SignRs256(payload),
            Config(issuer: "https://auth.example.com", audience: "my-api"));

        Assert.True(ok);
    }

    [Fact]
    public async Task SignedGarbagePayload_ReturnsFalse_NotThrow()
    {
        // Correctly signed token whose payload is not JSON — must be a validation
        // failure, not an unhandled exception (used to bubble up as a 500).
        var (ok, _, _) = await RsaHandler()
            .TryValidateAsync(AsymmetricTokenKit.SignRs256("not json"), Config());

        Assert.False(ok);
    }
}

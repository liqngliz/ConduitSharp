using System.Text;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using ConduitSharp.Security.Jwt;
using ConduitSharp.Security.Tests.Helpers;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class JwtAuthHandlerTests
{
    private readonly JwtAuthHandler _handler = new();

    private static JwtProviderConfig ConfigFor(
        string? secret = null,
        string? issuer = null,
        string? audience = null,
        string algorithm = "HS256") =>
        new()
        {
            SigningKey = secret ?? JwtTokenBuilder.DefaultSecretBase64,
            Algorithm  = algorithm,
            Issuer     = issuer,
            Audience   = audience
        };

    // -------------------------------------------------------------------------
    // Structure / malformed input — must fail validation, never throw
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("notavalidtoken")]
    [InlineData("header.payload")]
    [InlineData("a.b.c.d")]
    [InlineData("header.!!!invalid-payload!!!.sig")]
    public void Malformed_ReturnsFalse(string token)
    {
        var result = _handler.TryValidate(token, ConfigFor(), out var error, out _);

        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidBase64ButInvalidJsonPayload_ReturnsFalse()
    {
        // Valid Base64Url that decodes to non-JSON must fail validation, not throw
        // (used to bubble up as a 500).
        var garbage = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes("not json"));
        var header  = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));

        var result = _handler.TryValidate($"{header}.{garbage}.sig", ConfigFor(), out var error, out _);

        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void SignedTokenWithNonIntegerExp_ReturnsFalse_NotThrow()
    {
        var token = JwtTokenBuilder.ValidToken(extraClaims: new() { ["exp"] = "1700000000" });

        var result = _handler.TryValidate(token, ConfigFor(), out _, out _);

        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Algorithm / signature checks
    // -------------------------------------------------------------------------

    [Fact]
    public void UnsupportedConfigAlgorithm_ReturnsFalse()
    {
        var result = _handler.TryValidate(
            JwtTokenBuilder.ValidToken(), ConfigFor(algorithm: "RS256"), out var error, out _);

        Assert.False(result);
        Assert.Contains("signature", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlgNoneToken_ReturnsFalse()
    {
        // alg-confusion guard: an unsigned "none" token must never validate.
        var result = _handler.TryValidate(
            AsymmetricTokenKit.UnsignedToken("none"), ConfigFor(), out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void InvalidBase64SigningKey_ReturnsFalse()
    {
        var result = _handler.TryValidate(
            JwtTokenBuilder.ValidToken(), ConfigFor(secret: "not-valid-base64!!!"), out var error, out _);

        Assert.False(result);
        Assert.Contains("signature", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrongSigningKey_ReturnsFalse()
    {
        var result = _handler.TryValidate(
            JwtTokenBuilder.BadSignatureToken(), ConfigFor(), out var error, out _);

        Assert.False(result);
        Assert.Contains("signature", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidSignature_ReturnsTrue_AndExposesClaims()
    {
        var token = JwtTokenBuilder.ValidToken();

        var result = _handler.TryValidate(token, ConfigFor(), out var error, out var claims);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal("test-subject", claims.GetProperty("sub").GetString());
    }

    // -------------------------------------------------------------------------
    // Lifetime
    // -------------------------------------------------------------------------

    [Fact]
    public void ExpiredToken_ReturnsFalse()
    {
        var result = _handler.TryValidate(
            JwtTokenBuilder.ExpiredToken(), ConfigFor(), out var error, out _);

        Assert.False(result);
        Assert.Equal("Token has expired.", error);
    }

    [Fact]
    public void NotYetValidToken_ReturnsFalse()
    {
        var result = _handler.TryValidate(
            JwtTokenBuilder.NotYetValidToken(), ConfigFor(), out var error, out _);

        Assert.False(result);
        Assert.Equal("Token not yet valid.", error);
    }

    [Fact]
    public void TokenWithoutExp_IsValid()
    {
        // Pre-existing gateway behavior: exp is honored when present but not required.
        var token = JwtTokenBuilder.Build(JwtTokenBuilder.DefaultSecretBase64);

        Assert.True(_handler.TryValidate(token, ConfigFor(), out _, out _));
    }

    // -------------------------------------------------------------------------
    // Issuer / audience
    // -------------------------------------------------------------------------

    [Fact]
    public void WrongIssuer_ReturnsFalse()
    {
        var token = JwtTokenBuilder.ValidToken(issuer: "https://other.example.com");

        var result = _handler.TryValidate(
            token, ConfigFor(issuer: "https://auth.example.com"), out var error, out _);

        Assert.False(result);
        Assert.Equal("Invalid issuer.", error);
    }

    [Fact]
    public void WrongAudience_ReturnsFalse()
    {
        var token = JwtTokenBuilder.ValidToken(audience: "other-api");

        var result = _handler.TryValidate(
            token, ConfigFor(audience: "my-api"), out var error, out _);

        Assert.False(result);
        Assert.Equal("Invalid audience.", error);
    }

    [Fact]
    public void MissingAudienceWhenRequired_ReturnsFalse()
    {
        var result = _handler.TryValidate(
            JwtTokenBuilder.ValidToken(), ConfigFor(audience: "my-api"), out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void AudienceArrayContainingExpected_ReturnsTrue()
    {
        var token = JwtTokenBuilder.Build(
            JwtTokenBuilder.DefaultSecretBase64,
            audiences: ["other-api", "my-api"],
            exp: DateTimeOffset.UtcNow.AddHours(1));

        Assert.True(_handler.TryValidate(token, ConfigFor(audience: "my-api"), out _, out _));
    }

    [Fact]
    public void MatchingIssuerAndAudience_ReturnsTrue()
    {
        var token = JwtTokenBuilder.ValidToken(issuer: "https://auth.example.com", audience: "my-api");

        Assert.True(_handler.TryValidate(
            token, ConfigFor(issuer: "https://auth.example.com", audience: "my-api"), out _, out _));
    }
}

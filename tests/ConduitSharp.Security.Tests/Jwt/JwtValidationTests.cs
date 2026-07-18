using ConduitSharp.Security.Jwt;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ConduitSharp.Security.Tests.Jwt;

/// <summary>
/// Direct tests for <see cref="JwtValidation.MapError"/> — the map from
/// Microsoft.IdentityModel exceptions to stable, client-facing error strings.
/// The happy validation paths are covered by the handler tests; these pin the
/// error-string contract, including the fallbacks that the handler tests never hit.
/// </summary>
public class JwtValidationTests
{
    [Fact]
    public void MapError_Expired_ReturnsExpiredString() =>
        Assert.Equal("Token has expired.",
            JwtValidation.MapError(new SecurityTokenExpiredException()));

    [Fact]
    public void MapError_InvalidAlgorithm_ReturnsAlgorithmString() =>
        Assert.Equal("Invalid token algorithm.",
            JwtValidation.MapError(new SecurityTokenInvalidAlgorithmException("bad-alg")));

    [Fact]
    public void MapError_Null_ReturnsGenericFailure() =>
        Assert.Equal("Token validation failed.", JwtValidation.MapError(null));

    [Fact]
    public void MapError_UnknownException_ReturnsGenericFailure() =>
        Assert.Equal("Token validation failed.",
            JwtValidation.MapError(new InvalidOperationException("boom")));
}

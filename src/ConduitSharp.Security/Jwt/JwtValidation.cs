using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ConduitSharp.Security.Jwt;

/// <summary>
/// Shared glue between the two auth handlers and Microsoft.IdentityModel's
/// <see cref="JsonWebTokenHandler"/>: runs validation, maps library exceptions to the
/// gateway's stable client-facing error strings, and extracts the verified payload as a
/// <see cref="JsonElement"/> for <see cref="RequiredClaimsValidator"/>.
/// </summary>
internal static class JwtValidation
{
    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>
    /// Validates <paramref name="token"/> against <paramref name="parameters"/>.
    /// Returns <c>(true, null, claims)</c> on success; <c>(false, error, default)</c> otherwise.
    /// </summary>
    internal static async Task<(bool Success, string? Error, JsonElement Claims)> ValidateAsync(
        string token, TokenValidationParameters parameters)
    {
        TokenValidationResult result;
        try
        {
            result = await Handler.ValidateTokenAsync(token, parameters);
        }
        catch (Exception ex)
        {
            return (false, MapError(ex), default);
        }

        if (!result.IsValid)
            return (false, MapError(result.Exception), default);

        // The token is verified — its payload segment is safe to parse.
        JsonElement claims;
        using (var doc = JsonDocument.Parse(Base64UrlEncoder.Decode(((JsonWebToken)result.SecurityToken).EncodedPayload)))
            claims = doc.RootElement.Clone();
        return (true, null, claims);
    }

    /// <summary>Baseline validation parameters shared by both handlers.</summary>
    internal static TokenValidationParameters BaseParameters(string? issuer, string? audience) =>
        new()
        {
            // exp/nbf are honored when present, but a token without exp stays valid
            // (pre-existing gateway behavior; some internal issuers omit exp).
            RequireExpirationTime = false,
            ValidateIssuer        = issuer is not null,
            ValidIssuer           = issuer,
            ValidateAudience      = audience is not null,
            ValidAudience         = audience,
        };

    /// <summary>Maps Microsoft.IdentityModel exceptions to stable client-facing error strings.</summary>
    internal static string MapError(Exception? ex) => ex switch
    {
        SecurityTokenExpiredException              => "Token has expired.",
        SecurityTokenNotYetValidException          => "Token not yet valid.",
        // Covers SecurityTokenSignatureKeyNotFoundException too (derived type).
        SecurityTokenInvalidSignatureException     => "Invalid token signature.",
        SecurityTokenInvalidIssuerException        => "Invalid issuer.",
        SecurityTokenInvalidAudienceException      => "Invalid audience.",
        SecurityTokenInvalidAlgorithmException     => "Invalid token algorithm.",
        SecurityTokenMalformedException            => "Malformed token.",
        ArgumentException                          => "Malformed token.",
        null                                       => "Token validation failed.",
        _                                          => "Token validation failed.",
    };
}

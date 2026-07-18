using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace ConduitSharp.Security.Jwt;

/// <summary>
/// Validates HS256-signed JWTs via Microsoft.IdentityModel's
/// <c>JsonWebTokenHandler</c> (alg-confusion guards, clock-skew leeway, and a
/// decade of CVE hardening) rather than hand-rolled crypto.
/// </summary>
public sealed class JwtAuthHandler
{
    /// <summary>
    /// Validates <paramref name="token"/> against <paramref name="config"/>.
    /// Returns <c>true</c> on success; sets <paramref name="error"/> on failure.
    /// On success, <paramref name="claims"/> holds the verified payload; on failure
    /// it is <see langword="default"/>.
    /// </summary>
    public bool TryValidate(string token, JwtProviderConfig config, out string? error, out JsonElement claims)
    {
        claims = default;

        // Fail closed on a non-HS256 config or a signing key that isn't valid base64 —
        // same outcome as a signature that doesn't verify.
        byte[] keyBytes;
        try
        {
            if (!config.Algorithm.Equals("HS256", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException();
            keyBytes = Convert.FromBase64String(Pad(config.SigningKey));
        }
        catch
        {
            error = "Invalid token signature.";
            return false;
        }

        var parameters = JwtValidation.BaseParameters(config.Issuer, config.Audience);
        parameters.IssuerSigningKey = new SymmetricSecurityKey(keyBytes);
        parameters.ValidAlgorithms  = ["HS256"];

        // Completes synchronously for symmetric keys — no I/O involved.
        var (success, validationError, validatedClaims) =
            JwtValidation.ValidateAsync(token, parameters).GetAwaiter().GetResult();

        error  = validationError;
        claims = validatedClaims;
        return success;
    }

    private static string Pad(string s) => (s.Length % 4) switch
    {
        2 => s + "==",
        3 => s + "=",
        _ => s
    };
}

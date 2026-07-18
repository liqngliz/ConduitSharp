using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ConduitSharp.Security.Jwt;

/// <summary>
/// Validates asymmetrically signed JWTs (RS256/384/512, ES256/384/512) whose public keys
/// are fetched from a JWKS endpoint by <see cref="JwksKeyProvider"/>. Signature and claim
/// validation is delegated to Microsoft.IdentityModel's <c>JsonWebTokenHandler</c>.
/// </summary>
public sealed class JwksJwtAuthHandler
{
    private static readonly string[] SupportedAlgorithms =
        ["RS256", "RS384", "RS512", "ES256", "ES384", "ES512"];

    private readonly JwksConfigurationManagerFactory _factory;

    public JwksJwtAuthHandler(JwksConfigurationManagerFactory factory) =>
        _factory = factory;

    /// <summary>
    /// On success, <c>Claims</c> holds the verified payload; on failure it is
    /// <see langword="default"/>.
    /// </summary>
    public async Task<(bool Success, string? Error, JsonElement Claims)> TryValidateAsync(
        string token, JwksProviderConfig config, CancellationToken ct = default)
    {
        // Parse just enough to know which key to fetch (alg + kid); full validation
        // happens below once the key is in hand.
        JsonWebToken jwt;
        try { jwt = new JsonWebToken(token); }
        catch { return Fail("Malformed token."); }

        var alg = string.IsNullOrEmpty(jwt.Alg) ? null : jwt.Alg;
        if (!SupportedAlgorithms.Contains(alg, StringComparer.Ordinal))
            return Fail($"Unsupported algorithm '{alg}'. Supported: RS256, RS384, RS512, ES256, ES384, ES512.");

        var kid = string.IsNullOrEmpty(jwt.Kid) ? null : jwt.Kid;
        System.Collections.Generic.ICollection<Microsoft.IdentityModel.Tokens.SecurityKey> keys;
        try
        {
            var ttl = TimeSpan.FromSeconds(config.CacheTtlSeconds);
            // We set timeout on the HTTP client during factory construction or via the CancellationToken
            var timeout = TimeSpan.FromMilliseconds(config.JwksTimeoutMs);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            
            var manager = _factory.GetManager(config.JwksUri, ttl);
            // Force timeout enforcement even if ConfigurationManager swallows the CancellationToken internally
            var configuration = await manager.GetConfigurationAsync(CancellationToken.None).WaitAsync(cts.Token);
            keys = configuration.GetSigningKeys();
        }
        catch (Exception ex) { return Fail($"Failed to fetch JWKS: {ex.Message}"); }

        if (keys is null || keys.Count == 0)
            return Fail("No signing keys found in JWKS.");

        // If kid is provided, verify it exists in the downloaded keys
        if (kid is not null && !keys.Any(k => string.Equals(k.KeyId, kid, StringComparison.Ordinal)))
            return Fail($"No key with kid '{kid}' found in JWKS.");

        var parameters = JwtValidation.BaseParameters(config.Issuer, config.Audience);
        parameters.IssuerSigningKeys = keys;
        parameters.ValidAlgorithms  = SupportedAlgorithms;
        return await JwtValidation.ValidateAsync(token, parameters);
    }



    private static (bool, string?, JsonElement) Fail(string error) => (false, error, default);
}

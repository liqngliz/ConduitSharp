using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Security.Jwt;

/// <summary>
/// Validates a Bearer JWT in the <c>Authorization</c> header.
/// Short-circuits with 401 if the token is absent or invalid.
///
/// routes.json config block:
/// <code>
/// {
///   "signingKey": "&lt;base64-encoded HMAC-SHA256 secret&gt;",
///   "algorithm":  "HS256",
///   "issuer":     "https://my-idp.example.com",   // optional
///   "audience":   "my-api",                        // optional
///   "requiredClaims": [ { "claim": "roles", "anyOf": ["Admin"] } ]  // optional RBAC, see RequiredClaim
/// }
/// </code>
/// </summary>
public sealed class JwtAuthPlugin(JwtAuthHandler handler) : IPipelinePlugin
{
    public PluginName Name => PluginName.JwtAuth;
    public string Id => Name.ToId();

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        var config = JwtAuthConfig.From(configElement);

        var authHeader = context.Request.Headers.TryGetValue("Authorization", out var h) ? h.ToString() : null;
        var (token, extractError) = BearerTokenExtractor.Extract(authHeader);
        if (extractError is not null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync(extractError);
            return;
        }

        var lastStatusCode = 401;
        string lastError = "Unauthorized";

        foreach (var provider in config.EffectiveProviders)
        {
            if (!handler.TryValidate(token!, provider, out var authError, out var claims))
            {
                if (lastStatusCode != 403)
                {
                    lastStatusCode = 401;
                    lastError = authError!;
                }
                continue;
            }

            var claimsError = RequiredClaimsValidator.Validate(claims, provider.RequiredClaims);
            if (claimsError is not null)
            {
                lastStatusCode = 403;
                lastError = claimsError;
                continue;
            }

            // Successfully authenticated and authorized
            await next(context);
            return;
        }

        context.Response.StatusCode = lastStatusCode;
        await context.Response.WriteAsync(lastError);
    }

    // Loading the config validates requiredClaims shape, so a route with a malformed
    // requiredClaims block fails at startup instead of on the first request.
    public void ValidateConfig(JsonElement config) => JwtAuthConfig.From(config);
}

/// <summary>
/// Configuration for the <c>jwt-auth</c> plugin.
/// Validates HS256 Bearer JWTs. Place inside the route's <c>"config"</c> block.
/// </summary>
/// <example>
/// <code>
/// {
///   "name": "jwt-auth",
///   "order": 1,
///   "enabled": true,
///   "config": {
///     // base64 of the raw secret bytes, e.g. base64("demo-signing-key-conduitsharp-example-32ch")
///     "signingKey": "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo",
///     "algorithm": "HS256",
///     "issuer":    "https://auth.example.com",
///     "audience":  "my-api"
///   }
/// }
/// </code>
/// </example>
public sealed record JwtProviderConfig
{
    [JsonPropertyName("signingKey")] public string  SigningKey { get; init; } = "";
    [JsonPropertyName("algorithm")]  public string  Algorithm  { get; init; } = "HS256";
    [JsonPropertyName("issuer")]     public string? Issuer     { get; init; }
    [JsonPropertyName("audience")]   public string? Audience   { get; init; }
    [JsonPropertyName("requiredClaims")] public List<RequiredClaim>? RequiredClaims { get; init; }
}

public sealed record JwtAuthConfig
{
    // Legacy top-level fields
    [JsonPropertyName("signingKey")] public string? SigningKey { get; init; }
    [JsonPropertyName("algorithm")]  public string  Algorithm  { get; init; } = "HS256";
    [JsonPropertyName("issuer")]     public string? Issuer     { get; init; }
    [JsonPropertyName("audience")]   public string? Audience   { get; init; }
    [JsonPropertyName("requiredClaims")] public List<RequiredClaim>? RequiredClaims { get; init; }

    /// <summary>List of identity providers. If specified, the token is evaluated against each until one succeeds.</summary>
    [JsonPropertyName("providers")]  public List<JwtProviderConfig>? Providers { get; init; }

    [JsonIgnore]
    public IReadOnlyList<JwtProviderConfig> EffectiveProviders
    {
        get
        {
            if (Providers is { Count: > 0 }) return Providers;
            return [ new JwtProviderConfig {
                SigningKey = SigningKey!,
                Algorithm = Algorithm,
                Issuer = Issuer,
                Audience = Audience,
                RequiredClaims = RequiredClaims
            } ];
        }
    }

    public static JwtAuthConfig From(JsonElement raw)
    {
        var config = raw.Deserialize<JwtAuthConfig>(JsonOptions)
            ?? throw new InvalidOperationException("jwt-auth plugin config is null or invalid.");
            
        if (config.Providers is { Count: > 0 })
        {
            foreach (var p in config.Providers)
            {
                ValidateSigningKey(p.SigningKey);
                RequiredClaim.ValidateAll(p.RequiredClaims);
            }
        }
        else
        {
            ValidateSigningKey(config.SigningKey);
            RequiredClaim.ValidateAll(config.RequiredClaims);
        }
        
        return config;
    }

    // Startup guard for the classic interop landmine: a raw passphrase pasted into
    // signingKey either isn't valid base64 or decodes under the HS256 minimum, and
    // without this check every token is rejected at runtime with zero hint why.
    private static void ValidateSigningKey(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("jwt-auth: 'signingKey' is required in plugin config.");

        byte[] keyBytes;
        try
        {
            var padded = (signingKey.Length % 4) switch
            {
                2 => signingKey + "==",
                3 => signingKey + "=",
                _ => signingKey
            };
            keyBytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "jwt-auth: 'signingKey' must be the base64 encoding of the raw secret bytes " +
                "(the configured value is not valid base64).");
        }

        if (keyBytes.Length < 32)
            throw new InvalidOperationException(
                $"jwt-auth: 'signingKey' must decode to at least 32 bytes (256 bits) for HS256; " +
                $"it decodes to {keyBytes.Length} bytes.");
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

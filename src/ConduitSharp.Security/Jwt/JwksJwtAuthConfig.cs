using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConduitSharp.Security.Jwt;

/// <summary>
/// Configuration for the <c>jwks-jwt-auth</c> plugin.
/// Validates RS256 / ES256 Bearer JWTs by fetching public keys from a JWKS endpoint.
/// Compatible with Auth0, Azure AD, Google, Keycloak, and any OIDC-compliant provider.
/// Place inside the route's <c>"config"</c> block.
///
/// Lives apart from <see cref="JwksJwtAuthPlugin"/> because both the plugin and
/// <see cref="JwksJwtAuthHandler"/> read it: with the records declared in the plugin's file, the
/// handler's dependency on them pointed back at the plugin that depends on the handler.
/// </summary>
/// <example>
/// <code>
/// {
///   "name": "jwks-jwt-auth",
///   "order": 1,
///   "enabled": true,
///   "config": {
///     "jwksUri":         "https://your-tenant.auth0.com/.well-known/jwks.json",
///     "issuer":          "https://your-tenant.auth0.com/",
///     "audience":        "https://your-api.example.com",
///     "cacheTtlSeconds": 3600
///   }
/// }
/// </code>
/// </example>
public sealed record JwksProviderConfig
{
    [JsonPropertyName("jwksUri")]          public string  JwksUri         { get; init; } = "";
    [JsonPropertyName("issuer")]           public string? Issuer          { get; init; }
    [JsonPropertyName("audience")]         public string? Audience        { get; init; }
    [JsonPropertyName("cacheTtlSeconds")]  public int     CacheTtlSeconds { get; init; } = 3600;
    [JsonPropertyName("jwksTimeoutMs")]    public int     JwksTimeoutMs   { get; init; } = 5000;
    [JsonPropertyName("requiredClaims")]   public List<RequiredClaim>? RequiredClaims { get; init; }
}

public sealed record JwksJwtAuthConfig
{
    [JsonPropertyName("jwksUri")]          public string? JwksUri         { get; init; }
    [JsonPropertyName("issuer")]           public string? Issuer          { get; init; }
    [JsonPropertyName("audience")]         public string? Audience        { get; init; }
    [JsonPropertyName("cacheTtlSeconds")]  public int     CacheTtlSeconds { get; init; } = 3600;
    [JsonPropertyName("jwksTimeoutMs")]    public int     JwksTimeoutMs   { get; init; } = 5000;
    [JsonPropertyName("requiredClaims")]   public List<RequiredClaim>? RequiredClaims { get; init; }

    /// <summary>List of identity providers. If specified, the token is evaluated against each until one succeeds.</summary>
    [JsonPropertyName("providers")]        public List<JwksProviderConfig>? Providers { get; init; }

    [JsonIgnore]
    public IReadOnlyList<JwksProviderConfig> EffectiveProviders
    {
        get
        {
            if (Providers is { Count: > 0 }) return Providers;
            return [ new JwksProviderConfig { 
                JwksUri = JwksUri!, 
                Issuer = Issuer, 
                Audience = Audience, 
                CacheTtlSeconds = CacheTtlSeconds, 
                JwksTimeoutMs = JwksTimeoutMs, 
                RequiredClaims = RequiredClaims 
            } ];
        }
    }

    public static JwksJwtAuthConfig From(JsonElement raw)
    {
        var config = raw.Deserialize<JwksJwtAuthConfig>(JsonOptions)
            ?? throw new InvalidOperationException("jwks-jwt-auth plugin config is null or invalid.");
            
        if (config.Providers is { Count: > 0 })
        {
            foreach (var p in config.Providers)
            {
                if (string.IsNullOrWhiteSpace(p.JwksUri))
                    throw new InvalidOperationException("jwks-jwt-auth: 'jwksUri' is required on all providers.");
                RequiredClaim.ValidateAll(p.RequiredClaims);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(config.JwksUri))
                throw new InvalidOperationException("jwks-jwt-auth: 'jwksUri' is required when 'providers' is not used.");
            RequiredClaim.ValidateAll(config.RequiredClaims);
        }
        
        return config;
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Security.Jwt;

/// <summary>
/// Validates a Bearer JWT whose signature was produced by an asymmetric key
/// (RS256/384/512 or ES256/384/512). The public key is fetched automatically
/// from the configured JWKS endpoint and cached.
///
/// Use this plugin when integrating with a standard identity provider:
///   Auth0:    https://YOUR_TENANT.auth0.com/.well-known/jwks.json
///   Azure AD: https://login.microsoftonline.com/TENANT_ID/discovery/v2.0/keys
///   Google:   https://www.googleapis.com/oauth2/v3/certs
///   Keycloak: https://HOST/realms/REALM/protocol/openid-connect/certs
///
/// routes.json config block:
/// <code>
/// {
///   "jwksUri":         "https://your-tenant.auth0.com/.well-known/jwks.json",
///   "issuer":          "https://your-tenant.auth0.com/",   // optional but recommended
///   "audience":        "your-api-identifier",              // optional but recommended
///   "cacheTtlSeconds": 3600,                               // default: 1 hour
///   "requiredClaims":  [ { "claim": "roles", "anyOf": ["Admin"] } ]  // optional RBAC, see RequiredClaim
/// }
/// </code>
/// </summary>
public sealed class JwksJwtAuthPlugin(JwksJwtAuthHandler handler) : IPipelinePlugin
{
    public PluginName Name => PluginName.JwksJwtAuth;
    public string Id => Name.ToId();

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        var config = JwksJwtAuthConfig.From(configElement);

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
            var (success, authError, claims) = await handler.TryValidateAsync(token!, provider);
            if (!success)
            {
                if (lastStatusCode != 403) // Don't overwrite a 403 with a 401
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

    // Loading the config validates required fields (jwksUri), so a route with a missing
    // or malformed jwks-jwt-auth config fails at startup instead of on the first request.
    public void ValidateConfig(JsonElement config) => JwksJwtAuthConfig.From(config);
}

/// <summary>
/// Configuration for the <c>jwks-jwt-auth</c> plugin.
/// Validates RS256 / ES256 Bearer JWTs by fetching public keys from a JWKS endpoint.
/// Compatible with Auth0, Azure AD, Google, Keycloak, and any OIDC-compliant provider.
/// Place inside the route's <c>"config"</c> block.
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
    // Legacy top-level fields
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

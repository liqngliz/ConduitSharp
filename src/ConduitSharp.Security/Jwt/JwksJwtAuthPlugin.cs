using System.Text.Json;
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

            await next(context);
            return;
        }

        context.Response.StatusCode = lastStatusCode;
        await context.Response.WriteAsync(lastError);
    }

    public void ValidateConfig(JsonElement config) => JwksJwtAuthConfig.From(config);
}

using System.Text.Json;
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

            await next(context);
            return;
        }

        context.Response.StatusCode = lastStatusCode;
        await context.Response.WriteAsync(lastError);
    }

    public void ValidateConfig(JsonElement config) => JwtAuthConfig.From(config);
}

using System.Text.Json;
using System.Text.Json.Nodes;
using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway.Configuration;
using Microsoft.Extensions.Options;
using ConduitSharp.Gateway.Routing;

namespace ConduitSharp.Gateway.Swagger;

/// <summary>
/// Thrown when a route's specFile resolves outside Gateway:BasePath (path traversal).
/// Mapped to 400 with a generic body — the resolved path is never echoed to the client.
/// </summary>
internal sealed class SpecPathOutsideBasePathException : Exception;

/// <summary>
/// Thrown when a route's fetchFrom host is not allowlisted (SSRF guard).
/// Mapped to 403 with a generic body, before any network call is made.
/// </summary>
internal sealed class SpecHostNotAllowedException : Exception;

internal static class SwaggerAggregationExtensions
{
    /// <summary>
    /// Registers the aggregated Swagger UI and per-route spec endpoints.
    /// Must be called BEFORE UseMiddleware&lt;GatewayMiddleware&gt;() since the
    /// gateway middleware is terminal and never calls next.
    ///
    /// Routes with "swagger": { "fetchFrom": "..." } have their spec fetched
    /// live from the upstream on each request.
    /// Routes with "swagger": { "specFile": "..." } serve a local JSON file.
    ///
    /// Security schemes are injected automatically based on the route's plugin list:
    ///   api-key-auth / api-key-auth-hashed → OpenAPI apiKey scheme
    ///   jwt-auth / jwks-jwt-auth           → OpenAPI http bearer scheme
    ///
    /// UI is available at /swagger — disabled automatically when no routes
    /// have a swagger block configured.
    /// </summary>
    internal static void UseSwaggerAggregation(
        this IApplicationBuilder app,
        IReadOnlyList<GatewayRoute> routes,
        string? pathPrefix)
    {
        var swaggerRoutes = routes.Where(r => r.Swagger is not null).ToList();
        if (swaggerRoutes.Count == 0) return;

        var prefix = string.IsNullOrWhiteSpace(pathPrefix) ? "" : $"/{pathPrefix.Trim('/')}";
        var swaggerPath = $"{prefix}/swagger";

        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments(swaggerPath, out var remainder)
                || !remainder.Value!.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                await next(ctx);
                return;
            }

            var routeId = Path.GetFileNameWithoutExtension(remainder.Value.TrimStart('/'));
            var route = swaggerRoutes.FirstOrDefault(
                r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));

            if (route?.Swagger is null)
            {
                await next(ctx);
                return;
            }

            try
            {
                var json = await ResolveSpecAsync(ctx, route, prefix);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(json);
            }
            catch (SpecPathOutsideBasePathException)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync(
                    $"Invalid OpenAPI spec configuration for route '{routeId}'.");
            }
            catch (SpecHostNotAllowedException)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync(
                    $"Invalid OpenAPI spec configuration for route '{routeId}'.");
            }
            catch (Exception ex)
            {
                ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ConduitSharp.Gateway.Swagger")
                    .LogWarning(ex, "Failed to retrieve OpenAPI spec for route {RouteId}.", routeId);

                ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
                await ctx.Response.WriteAsync(
                    $"Failed to retrieve OpenAPI spec for route '{routeId}'.");
            }
        });

        app.UseSwaggerUI(ui =>
        {
            foreach (var route in swaggerRoutes)
            {
                var label = string.IsNullOrWhiteSpace(route.Description)
                    ? route.Id
                    : $"{route.Id} — {route.Description}";

                ui.SwaggerEndpoint($"{swaggerPath}/{route.Id}.json", label);
            }

            ui.RoutePrefix = swaggerPath.TrimStart('/');
            ui.DocumentTitle = "ConduitSharp API";
        });
    }

    private static async Task<string> ResolveSpecAsync(HttpContext ctx, GatewayRoute route, string prefix)
    {
        string json;

        if (route.Swagger!.FetchFrom is not null)
        {
            if (!Uri.TryCreate(route.Swagger.FetchFrom, UriKind.Absolute, out var target))
                throw new SpecHostNotAllowedException();

            var allowedHosts = ctx.RequestServices
                .GetRequiredService<IOptions<GatewayOptions>>().Value.Swagger.AllowedSpecHosts;

            var allowed =
                target.IsLoopback ||
                allowedHosts.Contains(target.Host, StringComparer.OrdinalIgnoreCase) ||
                route.Cluster?.Destinations?.Values.Any(destination =>
                    Uri.TryCreate(destination.Address, UriKind.Absolute, out var address) &&
                    string.Equals(address.Host, target.Host, StringComparison.OrdinalIgnoreCase)) == true;

            if (!allowed)
                throw new SpecHostNotAllowedException();

            var factory = ctx.RequestServices.GetRequiredService<IHttpClientFactory>();
            var client  = factory.CreateClient();
            json = await client.GetStringAsync(target);
        }
        else if (route.Swagger.SpecFile is not null)
        {
            var basePath = ctx.RequestServices
                .GetRequiredService<IOptions<GatewayOptions>>().Value.BasePath;

            var path = Path.IsPathRooted(route.Swagger.SpecFile)
                ? Path.GetFullPath(route.Swagger.SpecFile)
                : Path.GetFullPath(Path.Combine(basePath, route.Swagger.SpecFile));

            var root = Path.GetFullPath(basePath)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.Ordinal))
                throw new SpecPathOutsideBasePathException();

            json = await File.ReadAllTextAsync(path);
        }
        else
        {
            throw new InvalidOperationException(
                "SwaggerOptions requires either 'fetchFrom' or 'specFile' to be set.");
        }

        if (JsonNode.Parse(json) is not JsonObject doc) return json;

        var bearerDescription = ctx.RequestServices
            .GetRequiredService<IOptions<GatewayOptions>>().Value.Swagger.BearerDescription;

        doc["servers"] = new JsonArray(new JsonObject { ["url"] = prefix });
        InjectSecurityFromPlugins(doc, route.Plugins, bearerDescription);

        return doc.ToJsonString();
    }

    /// <summary>
    /// Inspects the route's active plugins and injects matching OpenAPI
    /// security schemes + global security requirements into the spec document.
    /// Existing securitySchemes entries are preserved; only gateway-added ones
    /// are written (so hand-crafted specFile entries still take effect).
    /// </summary>
    private static void InjectSecurityFromPlugins(
        JsonObject doc, IEnumerable<PluginConfig> plugins, string bearerDescription)
    {
        var schemes  = new JsonObject();
        var security = new JsonArray();

        foreach (var plugin in plugins.Where(p => p.Enabled))
        {
            switch (plugin.Name)
            {
                case PluginName.ApiKeyAuth:
                case PluginName.ApiKeyAuthHashed:
                {
                    var headerName = "X-Api-Key";
                    if (plugin.Config.ValueKind == JsonValueKind.Object
                        && plugin.Config.TryGetProperty("header", out var h))
                        headerName = h.GetString() ?? headerName;

                    schemes["ApiKey"] = new JsonObject
                    {
                        ["type"]        = "apiKey",
                        ["in"]          = "header",
                        ["name"]        = headerName,
                        ["description"] = $"API key passed in the `{headerName}` request header."
                    };
                    security.Add(new JsonObject { ["ApiKey"] = new JsonArray() });
                    break;
                }

                case PluginName.JwtAuth:
                case PluginName.JwksJwtAuth:
                {
                    schemes["Bearer"] = new JsonObject
                    {
                        ["type"]        = "http",
                        ["scheme"]      = "bearer",
                        ["bearerFormat"]= "JWT",
                        ["description"] = bearerDescription
                    };
                    security.Add(new JsonObject { ["Bearer"] = new JsonArray() });
                    break;
                }
            }
        }

        if (schemes.Count == 0) return;

        var components = doc["components"]?.DeepClone().AsObject() ?? new JsonObject();
        components["securitySchemes"] = schemes;
        doc["components"] = components;
        doc["security"]   = security;
    }
}

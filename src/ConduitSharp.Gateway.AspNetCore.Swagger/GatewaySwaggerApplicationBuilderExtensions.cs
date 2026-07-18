using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway.Swagger;
using ConduitSharp.Gateway.Routing;

namespace ConduitSharp.Gateway;

/// <summary>
/// Adds the optional aggregated Swagger UI to an embedded gateway (the
/// <c>ConduitSharp.Gateway.AspNetCore.Swagger</c> add-on package). Split out of the core library
/// so embedders that don't want Swagger don't take a Swashbuckle dependency.
/// </summary>
public static class GatewaySwaggerApplicationBuilderExtensions
{
    /// <summary>
    /// Serves one Swagger UI at <c>/swagger</c>, aggregating each route's OpenAPI spec, plus a
    /// per-route spec endpoint at <c>/swagger/{routeId}.json</c>. A no-op when no route declares a
    /// <c>swagger</c> block. Resolves the route table from DI, so it just needs
    /// <see cref="GatewayServiceCollectionExtensions.AddConduitSharpGateway"/> to have run.
    ///
    /// Call this BEFORE <see cref="GatewayApplicationBuilderExtensions.UseConduitSharpGateway"/>,
    /// since the gateway middleware is terminal and never calls next.
    /// </summary>
    public static WebApplication UseConduitSharpGatewaySwagger(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var routes = app.Services.GetRequiredService<GatewayRoutesConfiguration>();
        var options = app.Services.GetRequiredService<ConduitSharpGatewayOptions>();
        app.UseSwaggerAggregation(routes.Routes, options.PathPrefix);
        return app;
    }
}

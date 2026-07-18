using ConduitSharp.Gateway.Routing;
using Yarp.ReverseProxy.Configuration;

namespace ConduitSharp.Gateway.Proxy;

/// <summary>
/// Hands routes.json's YARP half straight to YARP.
///
/// There is no projection here any more — <see cref="GatewayRoute.Route"/> and
/// <see cref="GatewayRoute.Cluster"/> already <em>are</em> <see cref="RouteConfig"/> and
/// <see cref="ClusterConfig"/>. All this does is fill in the parts a user should never have to
/// type twice, and wire the gateway's circuit breaker into YARP's passive health-check slot.
///
/// That is the point of the shape: a field-by-field translator is a layer that can disagree with
/// YARP (it once silently downgraded HTTP/2 and broke gRPC), and it has to grow every time YARP
/// grows a feature. Neither is true of a <c>with</c> expression.
///
/// Routes with no cluster (plugin-only, short-circuit routes) are skipped: YARP rejects a route
/// with no cluster before any middleware runs, so those are mapped as plain endpoints instead.
/// </summary>
internal static class YarpConfigTranslator
{
    internal static (List<RouteConfig> Routes, List<ClusterConfig> Clusters) Translate(
        GatewayRoutesConfiguration gatewayRoutes)
    {
        var routes   = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        for (var i = 0; i < gatewayRoutes.Routes.Count; i++)
        {
            var route = gatewayRoutes.Routes[i];
            if (route.Cluster is not { } cluster) continue;

            routes.Add(route.Route with
            {
                // Routes and clusters are 1:1, so both ids are the route's id — never re-typed.
                RouteId   = route.Id,
                ClusterId = route.Id,

                // Declaration order breaks overlaps: endpoint selection prefers the lowest Order,
                // so the first route declared in routes.json wins. An explicit Order still wins.
                Order = route.Route.Order ?? i,
            });

            clusters.Add(cluster with
            {
                ClusterId   = route.Id,
                HealthCheck = WithCircuitBreaker(cluster.HealthCheck, route.CircuitBreaker),
            });
        }

        return (routes, clusters);
    }

    // The circuit breaker is a passive health-check policy. Only its *enablement* crosses into
    // YARP — the threshold and cooldown are read from the route's own CircuitBreakerConfig, so
    // nothing rides along in ClusterConfig.Metadata as a string. Any active health check the user
    // configured is preserved.
    private static HealthCheckConfig? WithCircuitBreaker(
        HealthCheckConfig? configured, CircuitBreakerConfig? breaker)
    {
        if (breaker is not { Threshold: > 0 }) return configured;

        return new HealthCheckConfig
        {
            Active                      = configured?.Active,
            AvailableDestinationsPolicy = configured?.AvailableDestinationsPolicy,
            Passive = new PassiveHealthCheckConfig
            {
                Enabled = true,
                Policy  = ConsecutiveFailuresHealthPolicy.PolicyName,
            },
        };
    }
}

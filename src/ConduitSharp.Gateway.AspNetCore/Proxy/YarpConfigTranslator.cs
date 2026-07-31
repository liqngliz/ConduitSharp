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
    /// <summary>
    /// The passive health policy name, written into <see cref="PassiveHealthCheckConfig.Policy"/>
    /// below and answered to by <c>ConsecutiveFailuresHealthPolicy.Name</c>. It lives here, with the
    /// config that references it, rather than on the policy: the policy already depends on
    /// <see cref="GatewayRouteTable"/> for its thresholds, and that table calls this translator — so
    /// a constant owned by the policy closed a loop through all three. One constant, still one
    /// source of truth, no cycle. Two literals would have been the same cycle break at the cost of
    /// the compile-time link that keeps the two strings equal.
    /// </summary>
    internal const string ConsecutiveFailuresPolicyName = "ConsecutiveFailures";

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
                RouteId   = route.Id,
                ClusterId = route.Id,

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
                Policy  = ConsecutiveFailuresPolicyName,
            },
        };
    }
}

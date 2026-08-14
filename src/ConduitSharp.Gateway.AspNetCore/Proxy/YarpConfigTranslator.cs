using ConduitSharp.Gateway.Routing;
using Yarp.ReverseProxy.Configuration;

namespace ConduitSharp.Gateway.Proxy;

/// <summary>
/// Hands routes.json's YARP half straight to YARP. <see cref="GatewayRoute.Route"/> and
/// <see cref="GatewayRoute.Cluster"/> already are <see cref="RouteConfig"/> and
/// <see cref="ClusterConfig"/>, so this only fills in what a user should not have to type twice
/// and wires the gateway's circuit breaker into YARP's passive health-check slot.
///
/// <para>Routes with no cluster (plugin-only, short-circuit routes) are skipped and mapped as
/// plain endpoints: YARP rejects a clusterless route before any middleware runs.</para>
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

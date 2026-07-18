using System.Collections.Concurrent;
using ConduitSharp.Gateway.Routing;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace ConduitSharp.Gateway.Proxy;

/// <summary>
/// Passive health policy implementing routes.json's <c>circuitBreaker</c> block:
/// <c>threshold</c> consecutive failures against one node take it out of the load balancer's
/// rotation for <c>cooldownMs</c>, after which YARP lets one trial request through.
///
/// YARP's stock passive policy (<c>TransportFailureRate</c>) is rate-over-a-window, not
/// consecutive-count, so it cannot express the documented threshold. This one can, and is ~40
/// lines — the whole <c>NodeHealthTracker</c> / <c>ILoadBalancer</c> tree it replaces is gone.
///
/// Thresholds come from the route's own <see cref="CircuitBreakerConfig"/>, resolved by route id
/// (clusters are 1:1 with routes). Nothing rides on <c>ClusterConfig.Metadata</c>: gateway config
/// stays typed, on the gateway's side of the split, and a hot reload updates it with the rest.
///
/// A failure is an upstream 502/503/504 or a transport-level forwarder error. A client disconnect
/// is not the node's fault and is not counted.
/// </summary>
internal sealed class ConsecutiveFailuresHealthPolicy(
    IDestinationHealthUpdater healthUpdater,
    GatewayRouteTable routes)
    : IPassiveHealthCheckPolicy
{
    /// <summary>Name referenced from <c>PassiveHealthCheckConfig.Policy</c>.</summary>
    internal const string PolicyName = "ConsecutiveFailures";

    public string Name => PolicyName;

    // ponytail: never trimmed — bounded by (clusters x destinations), i.e. the size of routes.json.
    private readonly ConcurrentDictionary<(string Cluster, string Destination), int> _consecutiveFailures =
        new();

    public void RequestProxied(HttpContext context, ClusterState cluster, DestinationState destination)
    {
        // Client went away mid-flight — tells us nothing about the node's health.
        if (context.RequestAborted.IsCancellationRequested) return;

        // ClusterId == RouteId, so the gateway half of this route's config is one lookup away.
        if (routes.TryGetRoute(cluster.ClusterId) is not { CircuitBreaker: { } breaker }) return;
        if (breaker.Threshold <= 0) return;

        var key = (cluster.ClusterId, destination.DestinationId);

        var failed = context.Response.StatusCode is 502 or 503 or 504
                  || context.Features.Get<IForwarderErrorFeature>() is not null;

        if (!failed)
        {
            _consecutiveFailures.TryRemove(key, out _);
            return;
        }

        if (_consecutiveFailures.AddOrUpdate(key, 1, (_, count) => count + 1) < breaker.Threshold) return;

        // Reactivation resets the destination to Unknown, not Healthy, and leaves the counter at
        // the threshold: a node that fails its trial request opens again immediately, while one
        // that succeeds resets above.
        healthUpdater.SetPassive(
            cluster, destination, DestinationHealth.Unhealthy,
            TimeSpan.FromMilliseconds(breaker.CooldownMs));
    }
}

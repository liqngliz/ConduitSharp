using System.Collections.Concurrent;
using ConduitSharp.Gateway.Routing;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace ConduitSharp.Gateway.Proxy;

/// <summary>
/// Passive health policy implementing routes.json's <c>circuitBreaker</c> block: <c>threshold</c>
/// consecutive failures against one node take it out of rotation for <c>cooldownMs</c>, after
/// which YARP lets one trial request through.
///
/// <para>A failure is an upstream 502/503/504 or a transport-level forwarder error. A client
/// disconnect is not the node's fault and is not counted.</para>
/// </summary>
internal sealed class ConsecutiveFailuresHealthPolicy(
    IDestinationHealthUpdater healthUpdater,
    GatewayRouteTable routes)
    : IPassiveHealthCheckPolicy
{
    /// <summary>
    /// Answers to the name <see cref="YarpConfigTranslator"/> writes into
    /// <c>PassiveHealthCheckConfig.Policy</c>. The constant lives there, with the config that
    /// references it — owning it here closed a loop, since this policy depends on
    /// <see cref="GatewayRouteTable"/> and that table calls the translator.
    /// </summary>
    public string Name => YarpConfigTranslator.ConsecutiveFailuresPolicyName;

    private readonly ConcurrentDictionary<(string Cluster, string Destination), int> _consecutiveFailures =
        new();

    public void RequestProxied(HttpContext context, ClusterState cluster, DestinationState destination)
    {
        if (context.RequestAborted.IsCancellationRequested) return;

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

        healthUpdater.SetPassive(
            cluster, destination, DestinationHealth.Unhealthy,
            TimeSpan.FromMilliseconds(breaker.CooldownMs));
    }
}

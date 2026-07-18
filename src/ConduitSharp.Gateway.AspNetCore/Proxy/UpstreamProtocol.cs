using System.Net;
using System.Runtime.CompilerServices;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace ConduitSharp.Gateway.Proxy;

/// <summary>
/// Keeps HTTP/2 intact end-to-end, which is what gRPC needs.
///
/// A cluster's <see cref="ForwarderRequestConfig"/> is static, but the right outbound protocol is
/// not: it depends on how the client arrived. YARP's default (HTTP/2,
/// <see cref="HttpVersionPolicy.RequestVersionOrLower"/>) silently downgrades to HTTP/1.1 against a
/// cleartext upstream, because there is no ALPN to negotiate with — which is correct for a normal
/// HTTP/1.1 request and fatal for gRPC.
///
/// So for an inbound HTTP/2 request, swap in a cluster model that demands h2c prior knowledge
/// (<see cref="HttpVersionPolicy.RequestVersionExact"/>). Everything else — the HTTP client,
/// destinations, health, concurrency counters — is the cluster's own, and YARP's forwarder
/// middleware still runs.
/// </summary>
internal static class UpstreamProtocol
{
    // Keyed on the cluster model, so a config change (which produces a new model) drops the
    // derived one with it, and the HttpMessageInvoker is shared rather than rebuilt.
    private static readonly ConditionalWeakTable<ClusterModel, ClusterModel> Http2Variants = new();

    internal static Task NegotiateAsync(HttpContext context, RequestDelegate next)
    {
        if (HttpProtocol.IsHttp2(context.Request.Protocol)
            && context.Features.Get<IReverseProxyFeature>() is ReverseProxyFeature feature)
        {
            feature.Cluster = Http2Variants.GetValue(feature.Cluster, ExactHttp2);
        }

        return next(context);
    }

    private static ClusterModel ExactHttp2(ClusterModel cluster) => new(
        new ClusterConfig
        {
            ClusterId           = cluster.Config.ClusterId,
            LoadBalancingPolicy = cluster.Config.LoadBalancingPolicy,
            Destinations        = cluster.Config.Destinations,
            HealthCheck         = cluster.Config.HealthCheck,
            HttpClient          = cluster.Config.HttpClient,
            Metadata            = cluster.Config.Metadata,
            HttpRequest = new ForwarderRequestConfig
            {
                ActivityTimeout = cluster.Config.HttpRequest?.ActivityTimeout,
                Version         = HttpVersion.Version20,
                VersionPolicy   = HttpVersionPolicy.RequestVersionExact,
            },
        },
        cluster.HttpClient);
}

using System.Net;
using ConduitSharp.Gateway.Proxy;
using Microsoft.AspNetCore.Http;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace ConduitSharp.Integration.Tests.Proxy;

/// <summary>
/// The gRPC-keeping protocol swap. This logic is otherwise only exercised by the LegacyGateway e2e
/// suite (a real HTTP/2 client against a real gateway process), which coverage cannot see — and
/// it exists because of a real production-shaped bug: YARP's default silently downgraded HTTP/2
/// to HTTP/1.1 against a cleartext upstream and broke gRPC with a 400.
/// </summary>
public class UpstreamProtocolTests
{
    private static ClusterModel Cluster(TimeSpan? timeout = null) => new(
        new ClusterConfig
        {
            ClusterId           = "c1",
            LoadBalancingPolicy = "RoundRobin",
            HttpRequest         = timeout is { } t ? new ForwarderRequestConfig { ActivityTimeout = t } : null,
        },
        new HttpMessageInvoker(new SocketsHttpHandler()));

    private static (HttpContext Context, ReverseProxyFeature Feature) Http(string protocol, ClusterModel cluster)
    {
        var feature = new ReverseProxyFeature
        {
            Cluster = cluster,
            Route   = new RouteModel(new RouteConfig { RouteId = "r1", ClusterId = "c1" }, null, HttpTransformer.Default),
            AllDestinations = [],
        };
        var ctx = new DefaultHttpContext();
        ctx.Request.Protocol = protocol;
        ctx.Features.Set<IReverseProxyFeature>(feature);
        return (ctx, feature);
    }

    [Fact]
    public async Task InboundHttp2_SwapsInAnH2cPriorKnowledgeCluster()
    {
        var original = Cluster(timeout: TimeSpan.FromSeconds(7));
        var (ctx, feature) = Http("HTTP/2", original);

        await UpstreamProtocol.NegotiateAsync(ctx, _ => Task.CompletedTask);

        Assert.NotSame(original, feature.Cluster);
        var request = feature.Cluster.Config.HttpRequest!;
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);

        // Everything that is not the protocol is the cluster's own: the per-attempt timeout
        // survives, and the HttpMessageInvoker (connection pool) is shared, not rebuilt.
        Assert.Equal(TimeSpan.FromSeconds(7), request.ActivityTimeout);
        Assert.Same(original.HttpClient, feature.Cluster.HttpClient);
        Assert.Equal("c1", feature.Cluster.Config.ClusterId);
    }

    [Fact]
    public async Task InboundHttp11_LeavesTheClusterAlone()
    {
        var original = Cluster();
        var (ctx, feature) = Http("HTTP/1.1", original);

        await UpstreamProtocol.NegotiateAsync(ctx, _ => Task.CompletedTask);

        Assert.Same(original, feature.Cluster);
    }

    [Fact]
    public async Task DerivedClusterIsCachedPerModel_NotRebuiltPerRequest()
    {
        var original = Cluster();

        var (ctx1, f1) = Http("HTTP/2", original);
        var (ctx2, f2) = Http("HTTP/2", original);
        await UpstreamProtocol.NegotiateAsync(ctx1, _ => Task.CompletedTask);
        await UpstreamProtocol.NegotiateAsync(ctx2, _ => Task.CompletedTask);

        // Same source model → same derived model. A config reload produces a NEW ClusterModel,
        // which naturally gets a fresh derivative — that is the eviction strategy.
        Assert.Same(f1.Cluster, f2.Cluster);
    }

    [Fact]
    public async Task AlwaysCallsNext()
    {
        var called = 0;
        var (ctx, _) = Http("HTTP/2", Cluster());
        await UpstreamProtocol.NegotiateAsync(ctx, _ => { called++; return Task.CompletedTask; });

        var plain = new DefaultHttpContext(); // no proxy feature at all (plugin-only route)
        plain.Request.Protocol = "HTTP/2";
        await UpstreamProtocol.NegotiateAsync(plain, _ => { called++; return Task.CompletedTask; });

        Assert.Equal(2, called);
    }
}

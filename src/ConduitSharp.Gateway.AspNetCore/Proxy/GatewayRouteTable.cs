using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using ConduitSharp.Gateway.Routing;

namespace ConduitSharp.Gateway.Proxy;

/// <summary>
/// The gateway's live route state: per-route plugin chains, the route lookup, plugin-only
/// endpoints, retry pipelines, and YARP's in-memory route/cluster config. Everything an admin
/// reload has to swap together, swapped here in one place — which is what makes
/// <c>POST /admin/routes/reload</c> a hot reload instead of a host restart.
///
/// Order matters in <see cref="Load"/>: chains and lookups are replaced before YARP's config, so
/// a request matching a just-added YARP route always finds its chain. In-flight requests keep the
/// chain delegate and <see cref="GatewayRoute"/> they started with.
/// </summary>
internal sealed class GatewayRouteTable(UpstreamRetry retry, IProxyConfigProvider configProvider)
{
    private Func<GatewayRoute, RequestDelegate>? _chainFactory;

    private volatile Dictionary<string, RequestDelegate> _chains = new(StringComparer.OrdinalIgnoreCase);
    private volatile Dictionary<string, GatewayRoute> _routesById = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Endpoints for plugin-only routes (<c>"upstream": null</c>), which bypass YARP.</summary>
    internal PluginEndpointDataSource PluginEndpoints { get; } = new();

    internal RequestDelegate ChainFor(string routeId) => _chains[routeId];
    internal GatewayRoute RouteFor(string routeId) => _routesById[routeId];

    /// <summary>
    /// The gateway half of a route's config, or null if the id is unknown. Lets code reached from
    /// YARP's side (which only knows a cluster id) get back to typed ConduitSharp config without
    /// anything having to ride along in <c>ClusterConfig.Metadata</c>.
    /// </summary>
    internal GatewayRoute? TryGetRoute(string routeId) =>
        _routesById.GetValueOrDefault(routeId);

    /// <summary>
    /// Installs the chain compiler. Chains close over <c>IApplicationBuilder.New()</c>, which only
    /// exists once the app is built — so <c>UseConduitSharpGateway</c> provides it, and reloads
    /// reuse it.
    /// </summary>
    internal void Initialize(Func<GatewayRoute, RequestDelegate> chainFactory) =>
        _chainFactory = chainFactory;

    /// <summary>Compiles and swaps in the given route table. Called at startup and on admin reload.</summary>
    internal void Load(GatewayRoutesConfiguration gatewayRoutes)
    {
        var chainFactory = _chainFactory
            ?? throw new InvalidOperationException("GatewayRouteTable used before UseConduitSharpGateway.");

        _chains = gatewayRoutes.Routes.ToDictionary(
            r => r.Id, chainFactory, StringComparer.OrdinalIgnoreCase);
        _routesById = gatewayRoutes.Routes.ToDictionary(
            r => r.Id, StringComparer.OrdinalIgnoreCase);

        retry.Load(gatewayRoutes);
        PluginEndpoints.Update(BuildPluginOnlyEndpoints(gatewayRoutes));

        var (routes, clusters) = YarpConfigTranslator.Translate(gatewayRoutes);
        ((InMemoryConfigProvider)configProvider).Update(routes, clusters);
    }

    private List<Endpoint> BuildPluginOnlyEndpoints(GatewayRoutesConfiguration gatewayRoutes)
    {
        var endpoints = new List<Endpoint>();

        for (var i = 0; i < gatewayRoutes.Routes.Count; i++)
        {
            var route = gatewayRoutes.Routes[i];
            if (route.Cluster is not null) continue;

            var match = route.Route.Match;

            // Declaration order breaks overlaps, mirroring RouteConfig.Order on the YARP side.
            var builder = new RouteEndpointBuilder(
                _chains[route.Id],
                RoutePatternFactory.Parse(match.Path ?? "/{**catch-all}"),
                order: route.Route.Order ?? i)
            {
                DisplayName = route.Id,
            };
            builder.Metadata.Add(route);

            if (match.Methods is { Count: > 0 } methods)
                builder.Metadata.Add(new HttpMethodMetadata(methods));

            endpoints.Add(builder.Build());
        }

        return endpoints;
    }

    /// <summary>
    /// A mutable <see cref="EndpointDataSource"/>: the change token tells ASP.NET Core routing to
    /// re-read <see cref="Endpoints"/> after a reload — the same mechanism YARP uses for its own
    /// endpoints.
    /// </summary>
    internal sealed class PluginEndpointDataSource : EndpointDataSource
    {
        private readonly object _sync = new();
        private volatile IReadOnlyList<Endpoint> _endpoints = [];
        private CancellationTokenSource _changeSignal = new();

        public override IReadOnlyList<Endpoint> Endpoints => _endpoints;

        public override IChangeToken GetChangeToken()
        {
            lock (_sync)
                return new CancellationChangeToken(_changeSignal.Token);
        }

        internal void Update(IReadOnlyList<Endpoint> endpoints)
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                _endpoints = endpoints;
                previous = _changeSignal;
                _changeSignal = new CancellationTokenSource();
            }
            previous.Cancel();
            previous.Dispose();
        }
    }
}

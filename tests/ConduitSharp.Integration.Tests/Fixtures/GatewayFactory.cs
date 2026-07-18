using ConduitSharp.Core.Pipeline;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConduitSharp.Integration.Tests.Fixtures;

/// <summary>
/// Boots the full gateway stack against a FakeUpstream.
///
/// Usage:
///   await using var upstream = await FakeUpstream.StartAsync();
///   await using var factory  = await GatewayFactory.CreateAsync(upstream, routes);
///   var client = factory.CreateClient();
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    private readonly string _routesJsonPath;
    private readonly IReadOnlyList<IPipelinePlugin> _plugins;
    private readonly TimeSpan? _upstreamHttpTimeout;
    private readonly IDictionary<string, string?>? _settings;
    private readonly Action<IWebHostBuilder>? _configureWebHost;

    private GatewayFactory(
        string routesJsonPath,
        IEnumerable<IPipelinePlugin>? plugins = null,
        TimeSpan? upstreamHttpTimeout = null,
        IDictionary<string, string?>? settings = null,
        Action<IWebHostBuilder>? configureWebHost = null)
    {
        _routesJsonPath     = routesJsonPath;
        _plugins            = plugins?.ToList() ?? [];
        _upstreamHttpTimeout = upstreamHttpTimeout;
        _settings           = settings;
        _configureWebHost   = configureWebHost;
    }

    /// <summary>
    /// Creates and starts the gateway with a routes.json that points all
    /// upstream nodes to <paramref name="upstream"/>.
    /// </summary>
    /// <param name="upstream">The fake upstream to forward traffic to.</param>
    /// <param name="routesJson">
    /// Full routes.json content. Leave null to use the default single-route
    /// passthrough config (no plugins, all traffic → upstream).
    /// </param>
    /// <param name="plugins">
    /// Additional <see cref="IPipelinePlugin"/> instances to register with the
    /// gateway's DI container. Useful for testing plugin short-circuit behaviour.
    /// </param>
    /// <param name="upstreamHttpTimeout">
    /// Overrides the timeout on the named "upstream" HttpClient. Use a very short
    /// value (e.g. 150 ms) to exercise the 504 gateway-timeout path.
    /// </param>
    /// <param name="settings">
    /// Configuration overrides applied on top of the gateway's own sources,
    /// keyed by colon-separated path (e.g. <c>"Gateway:RequestLimits:MaxRequestBodyBytes"</c>).
    /// </param>
    public static async Task<GatewayFactory> CreateAsync(
        FakeUpstream upstream,
        string? routesJson = null,
        IEnumerable<IPipelinePlugin>? plugins = null,
        TimeSpan? upstreamHttpTimeout = null,
        IDictionary<string, string?>? settings = null,
        Action<IWebHostBuilder>? configureWebHost = null)
    {
        routesJson ??= DefaultRoutes(upstream.BaseUrl);

        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, routesJson);

        Environment.SetEnvironmentVariable("Gateway__RoutesPath", path);

        return new GatewayFactory(path, plugins, upstreamHttpTimeout, settings, configureWebHost);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        if (_settings is not null)
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(_settings));

        builder.ConfigureServices(services =>
        {
            if (_upstreamHttpTimeout.HasValue)
                services.AddHttpClient("upstream")
                        .ConfigureHttpClient(c => c.Timeout = _upstreamHttpTimeout.Value);

            foreach (var plugin in _plugins)
                services.AddSingleton<IPipelinePlugin>(plugin);
        });

        _configureWebHost?.Invoke(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_routesJsonPath))
            File.Delete(_routesJsonPath);
        Environment.SetEnvironmentVariable("Gateway__RoutesPath", null);
    }

    // -------------------------------------------------------------------------
    // Default test routes — no plugins, single catch-all, forwards everything
    // -------------------------------------------------------------------------

    public static string DefaultRoutes(string upstreamBaseUrl) => $$"""
        {
          "routes": [
            {
              "id": "test-passthrough",
              "description": "Integration test catch-all — no plugins",
              "route": { "match": { "path": "/{**catch-all}" } },
              "cluster": {
                "loadBalancingPolicy": "RoundRobin",
                "destinations": { "node-0": { "address": "{{upstreamBaseUrl}}" } },
                "httpRequest": { "activityTimeout": "00:00:05" }
              },
              "plugins": []
            }
          ]
        }
        """;
}

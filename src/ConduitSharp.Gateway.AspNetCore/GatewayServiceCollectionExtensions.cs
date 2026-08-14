using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway.Configuration;
using ConduitSharp.Gateway.Plugins;
using ConduitSharp.Gateway.Proxy;
using ConduitSharp.Gateway.Telemetry;
using ConduitSharp.Observability.Logging;
using ConduitSharp.Observability.Metrics;
using ConduitSharp.Observability.Telemetry;
using ConduitSharp.Security.ApiKey;
using ConduitSharp.Security.Jwt;
using ConduitSharp.Traffic.Caching;
using ConduitSharp.Traffic.RateLimiting;
using ConduitSharp.Transformation.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.LoadBalancing;
using ConduitSharp.Gateway.Routing;

namespace ConduitSharp.Gateway;

/// <summary>
/// Wires the ConduitSharp gateway into a <see cref="WebApplicationBuilder"/> so it can run
/// standalone or be embedded inside any ASP.NET Core / Kestrel host — the YARP
/// <c>AddReverseProxy()</c> model. Pair with
/// <see cref="GatewayApplicationBuilderExtensions.UseConduitSharpGateway"/>.
/// </summary>
public static class GatewayServiceCollectionExtensions
{
    /// <summary>
    /// Registers the gateway's configuration binding, HTTP clients, plugin pipeline, built-in
    /// plugins, route table, and (optionally) observability and external-plugin scanning.
    /// The bound <c>Gateway</c> configuration section must already be present on
    /// <see cref="WebApplicationBuilder.Configuration"/>.
    /// </summary>
    public static WebApplicationBuilder AddConduitSharpGateway(
        this WebApplicationBuilder builder,
        Action<ConduitSharpGatewayOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new ConduitSharpGatewayOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);

        builder.Services.Configure<GatewayOptions>(
            builder.Configuration.GetSection(options.ConfigurationSectionName));

        var gatewayOptions = builder.Configuration
            .GetSection(options.ConfigurationSectionName)
            .Get<GatewayOptions>() ?? new GatewayOptions();

        builder.WebHost.UseShutdownTimeout(
            TimeSpan.FromSeconds(gatewayOptions.ShutdownTimeoutSeconds));

        builder.Services.AddHttpContextAccessor();

        static void RejectRemovedRequestLimitKeys(IConfiguration configuration, string sectionName)
        {
            var requestLimits = configuration.GetSection($"{sectionName}:RequestLimits");
            if (requestLimits["MaxTotalBufferedBodyBytes"] is not null)
                throw new InvalidOperationException(
                    "Gateway:RequestLimits:MaxTotalBufferedBodyBytes was removed in v2.0.0. It counted RAM and disk " +
                    "together, which cannot be sized correctly for either. Replace it with " +
                    "'MaxDiskBufferedBodyBytes' (spill-file bytes, sized against free disk) and " +
                    "'MaxRamBufferedBodyBytes' (heap, sized against available memory).");

            if (requestLimits["MaxMemoryBufferedBodyBytes"] is not null)
                throw new InvalidOperationException(
                    "Gateway:RequestLimits:MaxMemoryBufferedBodyBytes was renamed in v2.0.0. Use " +
                    "'MaxRamBufferedBodyBytes' — same meaning, named to pair with 'MaxDiskBufferedBodyBytes'.");

            if (requestLimits["MemoryBufferThresholdBytes"] is not null)
                throw new InvalidOperationException(
                    "Gateway:RequestLimits:MemoryBufferThresholdBytes was renamed in v2.0.0. Use " +
                    "'RamBufferThresholdBytes' — same meaning.");
        }

        builder.Services.AddHttpClient("jwks");

        AddPipelineAndBuiltInPlugins(builder.Services);

        if (options.ConfigureObservability)
            AddObservability(builder, gatewayOptions);

        var gatewayRoutes = LoadRoutes(options, gatewayOptions);
        ValidateTlsConfiguration(gatewayOptions, gatewayRoutes);
        builder.Services.AddSingleton(gatewayRoutes);
        builder.Services.AddSingleton<IReadOnlyList<GatewayRoute>>(gatewayRoutes.Routes);

        if (options.EnablePluginDirectoryScan)
            ScanPluginDirectory(builder.Services, gatewayOptions, gatewayRoutes);

        AddReverseProxy(builder.Services, gatewayRoutes);

        builder.Services.AddSingleton(sp =>
        {
            RejectRemovedRequestLimitKeys(
                sp.GetRequiredService<IConfiguration>(), options.ConfigurationSectionName);

            var limits = sp.GetRequiredService<IOptions<GatewayOptions>>().Value.RequestLimits;

            if (limits.MaxRamBufferedBodyBytes < 0)
                throw new InvalidOperationException(
                    $"Gateway:RequestLimits:MaxRamBufferedBodyBytes cannot be negative (was {limits.MaxRamBufferedBodyBytes}). " +
                    "Use 0 to allow no RAM buffering (every body spills to disk).");
            if (limits.MaxDiskBufferedBodyBytes < 0)
                throw new InvalidOperationException(
                    $"Gateway:RequestLimits:MaxDiskBufferedBodyBytes cannot be negative (was {limits.MaxDiskBufferedBodyBytes}). " +
                    "Use 0 to disallow spilling (a body must fit the RAM budget or be shed with 503).");
            if (limits.RamBufferThresholdBytes < 0)
                throw new InvalidOperationException(
                    $"Gateway:RequestLimits:RamBufferThresholdBytes cannot be negative (was {limits.RamBufferThresholdBytes}).");

            return new ConduitSharp.Gateway.Middleware.RequestBodyBudget(
                limits.MaxRamBufferedBodyBytes, limits.MaxDiskBufferedBodyBytes);
        });

        return builder;
    }

    private static void AddReverseProxy(IServiceCollection services, GatewayRoutesConfiguration gatewayRoutes)
    {
        var (routes, clusters) = YarpConfigTranslator.Translate(gatewayRoutes);

        services.AddReverseProxy()
                .LoadFromMemory(routes, clusters)
                .AddTransforms<SuppressRetriedResponseTransform>();

        services.AddSingleton<IForwarderHttpClientFactory, UpstreamForwarderHttpClientFactory>();
        services.AddSingleton<IPassiveHealthCheckPolicy, ConsecutiveFailuresHealthPolicy>();
        services.AddSingleton<UpstreamRetry>();
        services.AddSingleton<GatewayRouteTable>();
    }

    private static void AddPipelineAndBuiltInPlugins(IServiceCollection services)
    {
        services.AddSingleton<PluginAssemblyLoader>();

        services.AddSingleton<JwtAuthHandler>();
        services.AddSingleton<IPipelinePlugin, JwtAuthPlugin>();
        services.AddSingleton<JwksConfigurationManagerFactory>();
        services.AddSingleton<JwksJwtAuthHandler>();
        services.AddSingleton<IPipelinePlugin, JwksJwtAuthPlugin>();
        services.AddSingleton<IPipelinePlugin, ApiKeyAuthPlugin>();
        services.AddSingleton<IPipelinePlugin, ApiKeyAuthHashedPlugin>();
        services.AddSingleton<IPipelinePlugin, HeaderTransformPlugin>();
        services.AddSingleton<IRateLimitStore, InMemoryRateLimitStore>();
        services.AddSingleton<IRateLimiter>(sp => new FixedWindowRateLimiter(sp.GetRequiredService<IRateLimitStore>()));
        services.AddSingleton<IPipelinePlugin, RateLimitPlugin>();
        services.AddSingleton<ICacheService>(sp => new InMemoryCacheService(
            sp.GetRequiredService<IOptions<GatewayOptions>>().Value.Cache.MaxTotalBytes));
        services.AddSingleton<IPipelinePlugin, CachePlugin>();

    }

    private static void AddObservability(WebApplicationBuilder builder, GatewayOptions gatewayOptions)
    {
        builder.Services.AddSingleton<IRequestObserver, StructuredRequestLogger>();
        builder.Services.AddSingleton<IRequestObserver, OtelMetricsObserver>();

        var otlp    = gatewayOptions.Observability.Otlp;
        var console = gatewayOptions.Observability.Console;
        var file    = gatewayOptions.Observability.File;

        var otlpEndpoint = !string.IsNullOrEmpty(otlp.Endpoint)
            ? otlp.Endpoint
            : builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        var otlpEnabled = otlp.Enabled || !string.IsNullOrEmpty(otlpEndpoint);

        if (!otlpEnabled && !console.Enabled && !file.Enabled)
            return;

        if (otlpEnabled)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = false;
                logging.AddOtlpExporter(o =>
                {
                    if (!string.IsNullOrEmpty(otlp.Endpoint))
                        o.Endpoint = new Uri(otlp.Endpoint);
                });
            });
        }

        var otlpHost = string.IsNullOrEmpty(otlpEndpoint)
            ? null
            : new Uri(otlpEndpoint).Host;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("ConduitSharp.Gateway"))
            .WithTracing(t =>
            {
                t.AddSource(GatewayTelemetry.SourceName)
                 .AddSource(PipelineTelemetry.SourceName)
                 .AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation(o =>
                     o.FilterHttpRequestMessage = req =>
                         otlpHost is null || req.RequestUri?.Host != otlpHost);

                if (otlpEnabled)
                    t.AddOtlpExporter(o =>
                    {
                        if (!string.IsNullOrEmpty(otlp.Endpoint))
                            o.Endpoint = new Uri(otlp.Endpoint);
                    });

                if (console.Enabled)
                    t.AddConsoleExporter();

                if (file.Enabled)
                {
                    var tracesPath = Path.IsPathRooted(file.TracesPath)
                        ? file.TracesPath
                        : Path.GetFullPath(Path.Combine(gatewayOptions.BasePath, file.TracesPath));
                    t.AddProcessor(new SimpleActivityExportProcessor(new FileSpanExporter(tracesPath)));
                }
            })
            .WithMetrics(m =>
            {
                m.AddMeter(GatewayTelemetry.SourceName)
                 .AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation();

                if (otlpEnabled)
                    m.AddOtlpExporter(o =>
                    {
                        if (!string.IsNullOrEmpty(otlp.Endpoint))
                            o.Endpoint = new Uri(otlp.Endpoint);
                    });

                if (console.Enabled)
                    m.AddConsoleExporter();
            });
    }

    private static void ValidateTlsConfiguration(
        GatewayOptions gatewayOptions, GatewayRoutesConfiguration gatewayRoutes)
    {
        foreach (var cert in gatewayOptions.Tls.ClientCertificates)
        {
            var route = gatewayRoutes.Routes.FirstOrDefault(
                r => string.Equals(r.Id, cert.RouteId, StringComparison.OrdinalIgnoreCase));

            if (route?.Cluster?.HttpClient?.DangerousAcceptAnyServerCertificate == true)
                throw new InvalidOperationException(
                    $"Route '{cert.RouteId}' configures a client certificate (mTLS) but its cluster sets " +
                    "httpClient.dangerousAcceptAnyServerCertificate=true. Presenting a client certificate " +
                    "to a server you refuse to authenticate defeats the point of mTLS — it is mutual. " +
                    "Remove one of the two: they are mutually exclusive.");
        }
    }

    private static GatewayRoutesConfiguration LoadRoutes(
        ConduitSharpGatewayOptions options, GatewayOptions gatewayOptions)
    {
        if (options.Routes is not null)
        {
            options.Routes.Validate();
            return options.Routes;
        }

        var routesPath = options.RoutesPath ?? gatewayOptions.RoutesPath;
        var gatewayRoutes = GatewayRoutesConfiguration.Parse(File.ReadAllText(routesPath));
        gatewayRoutes.Validate();
        return gatewayRoutes;
    }

    private static void ScanPluginDirectory(
        IServiceCollection services,
        GatewayOptions gatewayOptions,
        GatewayRoutesConfiguration gatewayRoutes)
    {
        var pluginsDir = gatewayOptions.PluginsPath;

        using var bootstrap = LoggerFactory.Create(b => b.AddConsole());
        var bootstrapLogger = bootstrap.CreateLogger<PluginAssemblyLoader>();
        var loader = new PluginAssemblyLoader(bootstrapLogger);

        foreach (var type in loader.DiscoverPluginTypes(pluginsDir))
            services.AddSingleton(typeof(IPipelinePlugin), type);

        var cacheServiceType = loader.DiscoverServiceType<ICacheService>(pluginsDir);
        if (cacheServiceType is not null)
            services.AddSingleton(typeof(ICacheService), cacheServiceType);

        var rateLimitStoreType = loader.DiscoverServiceType<IRateLimitStore>(pluginsDir);
        if (rateLimitStoreType is not null)
            services.AddSingleton(typeof(IRateLimitStore), rateLimitStoreType);

        var rateLimiterType = loader.DiscoverServiceType<IRateLimiter>(pluginsDir);
        if (rateLimiterType is not null)
            services.AddSingleton(typeof(IRateLimiter), rateLimiterType);

        var matcherPolicyType = loader.DiscoverServiceType<MatcherPolicy>(pluginsDir);
        if (matcherPolicyType is not null)
            services.AddSingleton(typeof(MatcherPolicy), matcherPolicyType);

        var loadBalancingPolicyType = loader.DiscoverServiceType<ILoadBalancingPolicy>(pluginsDir);
        if (loadBalancingPolicyType is not null)
            services.AddSingleton(typeof(ILoadBalancingPolicy), loadBalancingPolicyType);
    }
}

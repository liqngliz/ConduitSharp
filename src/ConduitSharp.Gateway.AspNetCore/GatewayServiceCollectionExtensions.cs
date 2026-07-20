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

        // Stash the resolved composition options so UseConduitSharpGateway can read the same
        // toggles back after Build() without the caller having to pass them twice.
        builder.Services.AddSingleton(options);

        builder.Services.Configure<GatewayOptions>(
            builder.Configuration.GetSection(options.ConfigurationSectionName));

        var gatewayOptions = builder.Configuration
            .GetSection(options.ConfigurationSectionName)
            .Get<GatewayOptions>() ?? new GatewayOptions();

        // Drain in-flight requests on shutdown (including admin route reload) rather than
        // cutting them off mid-response.
        builder.WebHost.UseShutdownTimeout(
            TimeSpan.FromSeconds(gatewayOptions.ShutdownTimeoutSeconds));

        builder.Services.AddHttpContextAccessor();

        // Used by JwksJwtAuthPlugin to fetch the public key set from the identity provider.
        // Upstream forwarding no longer uses IHttpClientFactory — YARP builds one
        // HttpMessageInvoker per cluster (see UpstreamForwarderHttpClientFactory).
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
            var limits = sp.GetRequiredService<IOptions<GatewayOptions>>().Value.RequestLimits;
            // The memory tier is carved out of the total, so a memory limit above it would never
            // bind — clamp rather than reject, since the pair is usually set independently and the
            // safe reading of "memory 256 MiB, total 128 MiB" is "all 128 MiB may be RAM".
            var maxMemory = limits.MaxTotalBufferedBodyBytes > 0
                ? Math.Min(limits.MaxMemoryBufferedBodyBytes, limits.MaxTotalBufferedBodyBytes)
                : limits.MaxMemoryBufferedBodyBytes;

            return new ConduitSharp.Gateway.Middleware.RequestBodyBudget(
                limits.MaxTotalBufferedBodyBytes, maxMemory);
        });

        return builder;
    }

    // ---------------------------------------------------------------------------
    // The proxy engine. routes.json is translated into YARP's route/cluster model and served from
    // memory — YARP's own appsettings schema is never bound, so routes.json stays the product.
    // YARP validates each cluster's loadBalancingStrategy against the registered
    // ILoadBalancingPolicy set at load time, which is where an unknown strategy name is caught.
    // ---------------------------------------------------------------------------
    private static void AddReverseProxy(IServiceCollection services, GatewayRoutesConfiguration gatewayRoutes)
    {
        var (routes, clusters) = YarpConfigTranslator.Translate(gatewayRoutes);

        services.AddReverseProxy()
                .LoadFromMemory(routes, clusters)
                .AddTransforms<SuppressRetriedResponseTransform>();

        // Registered after AddReverseProxy so these win over YARP's TryAdd-ed defaults.
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

        // No http-proxy plugin: forwarding is YARP's ForwarderMiddleware, and the "http-proxy"
        // entry in a route's plugin list just names where in the chain that forward happens.
        // match.headers / match.queryParams are enforced natively by YARP's header and query
        // MatcherPolicies, built from RouteMatch.Headers / RouteMatch.QueryParameters.
    }

    private static void AddObservability(WebApplicationBuilder builder, GatewayOptions gatewayOptions)
    {
        builder.Services.AddSingleton<IRequestObserver, StructuredRequestLogger>();
        builder.Services.AddSingleton<IRequestObserver, OtelMetricsObserver>();

        // OpenTelemetry — traces and metrics.
        // Console exporter: Gateway:Observability:Console:Enabled=true (dev, no collector needed).
        // OTLP exporter:   Gateway:Observability:Otlp:Enabled=true    (production, e.g. Jaeger/Grafana).
        //
        // OTLP is also auto-enabled when the standard OTEL_EXPORTER_OTLP_ENDPOINT environment variable
        // is set (the OpenTelemetry / .NET Aspire convention), so the gateway "just works" under an
        // Aspire dashboard or any orchestrator that injects that variable — no Gateway__...__Enabled needed.
        var otlp    = gatewayOptions.Observability.Otlp;
        var console = gatewayOptions.Observability.Console;
        var file    = gatewayOptions.Observability.File;

        // Effective endpoint: explicit config wins, otherwise fall back to the SDK's standard env var.
        // When left null we let the OTel SDK resolve OTEL_EXPORTER_OTLP_ENDPOINT itself; we only read it
        // here so auto-enable and the self-tracing filter below know where the collector lives.
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
                // Scope capture + serialization runs on every log record and sits on the per-request
                // path. The gateway's useful context (route_id, path) is stamped straight onto records
                // by the plugins that need it (e.g. body-capture's PerRouteBodyLimitInterceptor via
                // AddParameter), not through the ASP.NET scope stack — so this drops cost, not signal.
                logging.IncludeScopes = false;
                // Batch export (the SDK default): spans/logs are queued and flushed in the
                // background instead of a synchronous network call per item — Simple is a
                // dev/debug setting and this export sits on a per-request latency path.
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

    // Fail fast on a route that configures BOTH a client certificate (mTLS) and
    // skipCertificateVerification: the latter selects the "upstream-insecure" HttpClient, which
    // carries no client certificate — so the cert would silently never be presented. The two are
    // mutually exclusive; catch it at startup rather than as a confusing runtime auth failure.
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
        // External plugins — one subdirectory per route under the plugins root (organizational
        // only; discovery is gateway-wide). SyncPluginDirectories creates missing per-route
        // folders and leaves everything else in place. DiscoverPluginTypes then scans each
        // subdirectory for IPipelinePlugin implementations.
        var pluginsDir = gatewayOptions.PluginsPath;

        using var bootstrap = LoggerFactory.Create(b => b.AddConsole());
        var bootstrapLogger = bootstrap.CreateLogger<PluginAssemblyLoader>();
        var loader = new PluginAssemblyLoader(bootstrapLogger);

        loader.SyncPluginDirectories(pluginsDir, gatewayRoutes.Routes);

        foreach (var type in loader.DiscoverPluginTypes(pluginsDir))
            services.AddSingleton(typeof(IPipelinePlugin), type);

        var cacheServiceType = loader.DiscoverServiceType<ICacheService>(pluginsDir);
        if (cacheServiceType is not null)
            services.AddSingleton(typeof(ICacheService), cacheServiceType);

        // A rate-limit-store DLL dropped in the plugins root (e.g. ConduitSharp.RateLimit.RedisProtocol)
        // overrides the built-in per-process InMemoryRateLimitStore — last DI registration wins —
        // giving the gateway rate limits shared across replicas without any core change.
        var rateLimitStoreType = loader.DiscoverServiceType<IRateLimitStore>(pluginsDir);
        if (rateLimitStoreType is not null)
            services.AddSingleton(typeof(IRateLimitStore), rateLimitStoreType);

        // Same seam one level up: a dropped-in IRateLimiter replaces the *algorithm* (e.g. sliding
        // window) rather than the counter backend. The two are independent — a drop-in algorithm
        // may use the registered store, or keep state a store cannot model, as a sliding log does.
        var rateLimiterType = loader.DiscoverServiceType<IRateLimiter>(pluginsDir);
        if (rateLimiterType is not null)
            services.AddSingleton(typeof(IRateLimiter), rateLimiterType);

        // A custom route-matching strategy dropped in as a MatcherPolicy DLL composes with the ones
        // YARP registers — ASP.NET Core collects every registered MatcherPolicy. This is the native
        // replacement for the old drop-in IRouteMatcher seam.
        var matcherPolicyType = loader.DiscoverServiceType<MatcherPolicy>(pluginsDir);
        if (matcherPolicyType is not null)
            services.AddSingleton(typeof(MatcherPolicy), matcherPolicyType);

        // A load-balancing policy DLL (YARP's ILoadBalancingPolicy) joins the built-in set — a
        // cluster opts in by naming it: "upstream": { "loadBalancingStrategy": "MyPolicy" }.
        var loadBalancingPolicyType = loader.DiscoverServiceType<ILoadBalancingPolicy>(pluginsDir);
        if (loadBalancingPolicyType is not null)
            services.AddSingleton(typeof(ILoadBalancingPolicy), loadBalancingPolicyType);
    }
}

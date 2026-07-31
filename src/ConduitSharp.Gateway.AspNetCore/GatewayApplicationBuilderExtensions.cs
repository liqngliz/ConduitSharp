using System.Diagnostics;
using System.Security.Cryptography;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway.Configuration;
using ConduitSharp.Gateway.Proxy;
using ConduitSharp.Observability.Telemetry;
using ConduitSharp.Traffic.Caching;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;
using ConduitSharp.Gateway.Routing;

namespace ConduitSharp.Gateway;

/// <summary>
/// Adds the gateway request-processing middleware (admin API, health endpoints, and the YARP-backed
/// proxy/plugin pipeline) to a <see cref="WebApplication"/>. Pair with
/// <see cref="GatewayServiceCollectionExtensions.AddConduitSharpGateway"/>. Aggregated Swagger UI
/// is an optional add-on — see the <c>ConduitSharp.Gateway.AspNetCore.Swagger</c> package.
/// </summary>
public static class GatewayApplicationBuilderExtensions
{
    /// <summary>
    /// Validates the loaded route configs, then wires the gateway according to the
    /// <see cref="ConduitSharpGatewayOptions"/> supplied to <c>AddConduitSharpGateway</c>.
    ///
    /// Each route becomes an endpoint: routes with an upstream are mapped through YARP
    /// (<c>MapReverseProxy</c>) with a per-route plugin chain compiled once at startup, and routes
    /// without one are mapped as plain plugin-only endpoints. When
    /// <see cref="ConduitSharpGatewayOptions.PathPrefix"/> is set the gateway also owns unmatched
    /// paths under that prefix (404 rather than falling through to the host).
    /// </summary>
    public static WebApplication UseConduitSharpGateway(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options        = app.Services.GetRequiredService<ConduitSharpGatewayOptions>();
        var gatewayOptions = app.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;
        var gatewayRoutes  = app.Services.GetRequiredService<GatewayRoutesConfiguration>();

        _ = app.Services.GetRequiredService<Middleware.RequestBodyBudget>();

        if (options.EnableAdminApi)
        {
            var routesPath = options.RoutesPath ?? gatewayOptions.RoutesPath;
            MapAdminApi(app, gatewayOptions.AdminKeyHash, routesPath);
        }

        if (options.MapHealthEndpoints)
            MapHealthEndpoints(app);

        var observers = app.Services.GetServices<IRequestObserver>().ToArray();
        app.Use(async (ctx, next) =>
        {
            using var activity = GatewayTelemetry.ActivitySource.StartActivity("gateway.request");
            activity?.SetTag("http.request.method", ctx.Request.Method);
            activity?.SetTag("url.path", ctx.Request.Path.Value);
            var startedAt = Stopwatch.GetTimestamp();

            try
            {
                await next(ctx);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
            finally
            {
                activity?.SetTag("http.response.status_code", ctx.Response.StatusCode);
                if (ctx.Response.StatusCode >= 500)
                    activity?.SetStatus(ActivityStatusCode.Error);

                if (observers.Length > 0)
                {
                    var observation = new RequestObservation(
                        ctx.TraceIdentifier,
                        ctx.Request.Method,
                        ctx.Request.Path.Value ?? "/",
                        ctx.Items.TryGetValue(GatewayItems.RouteId, out var id) ? (string?)id : null,
                        ctx.Response.StatusCode,
                        (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

                    foreach (var observer in observers)
                    {
                        try { observer.OnRequestCompleted(observation); }
                        catch { }
                    }
                }
            }
        });

        ValidateRouteTable(app.Services, gatewayRoutes);

        var table = app.Services.GetRequiredService<GatewayRouteTable>();
        table.Initialize(route => BuildRouteChain(app, route, gatewayOptions));
        table.Load(gatewayRoutes);

        app.MapReverseProxy(proxyPipeline =>
        {
            proxyPipeline.Use(async (HttpContext context, RequestDelegate next) =>
            {
                var routeId = context.GetReverseProxyFeature().Route.Config.RouteId;
                context.Items[GatewayItems.ProxyNext] = next;
                await table.ChainFor(routeId)(context);
            });

            proxyPipeline.Use(UpstreamProtocol.NegotiateAsync);
            proxyPipeline.Use(app.Services.GetRequiredService<UpstreamRetry>().InvokeAsync);
            proxyPipeline.UseLoadBalancing();
            proxyPipeline.UsePassiveHealthChecks();
        })
        .ConfigureEndpoints((builder, route) => builder.WithMetadata(table.RouteFor(route.RouteId)));

        ((IEndpointRouteBuilder)app).DataSources.Add(table.PluginEndpoints);

        return app;
    }

    private static void ValidateRouteTable(IServiceProvider services, GatewayRoutesConfiguration gatewayRoutes)
    {
        ValidateLoadBalancingPolicies(services, gatewayRoutes);
        ValidatePluginChains(services, gatewayRoutes);
    }

    private static void ValidateLoadBalancingPolicies(
        IServiceProvider services, GatewayRoutesConfiguration gatewayRoutes)
    {
        var registered = services.GetServices<ILoadBalancingPolicy>()
            .Select(policy => policy.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var route in gatewayRoutes.Routes)
        {
            if (route.Cluster?.LoadBalancingPolicy is not { Length: > 0 } policy) continue;
            if (registered.Contains(policy)) continue;

            throw new InvalidOperationException(
                $"Route '{route.Id}': unknown loadBalancingPolicy '{policy}'. " +
                $"Available policies: {string.Join(", ", registered.Order(StringComparer.Ordinal))}. " +
                "Drop an ILoadBalancingPolicy DLL into the plugins root to add your own.");
        }
    }

    private static void ValidatePluginChains(IServiceProvider services, GatewayRoutesConfiguration gatewayRoutes)
    {
        var allPlugins    = services.GetServices<IPipelinePlugin>().ToList();
        var startupLogger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("ConduitSharp.Gateway.Plugins.Plugin");
        var loggedPlugins = new HashSet<string>();

        foreach (var route in gatewayRoutes.Routes)
        {
            var enabledPlugins = route.Plugins.Where(p => p.Enabled).OrderBy(p => p.Order).ToList();

            var proxyIndex = enabledPlugins.FindIndex(p => p.Name == PluginName.HttpProxy);
            if (proxyIndex >= 0 && proxyIndex != enabledPlugins.Count - 1)
                throw new InvalidOperationException($"Route '{route.Id}': 'http-proxy' must be the last enabled plugin.");

            foreach (var pluginConfig in enabledPlugins)
            {
                if (pluginConfig.Name == PluginName.HttpProxy) continue;

                var plugin = ResolvePlugin(allPlugins, pluginConfig)
                    ?? throw new InvalidOperationException(
                        $"Route '{route.Id}': no plugin registered for '{pluginConfig.Name}'.");

                if (route.StreamOnly && plugin.ReadsRequestBody)
                    throw new InvalidOperationException(
                        $"Route '{route.Id}': plugin '{pluginConfig.Name}' reads the request body, which requires " +
                        "the buffered body the gateway provides — it cannot run on a streamOnly route. " +
                        "Remove streamOnly from this route, or the body-reading plugin.");

                var pluginId = pluginConfig.Name == PluginName.Custom
                    ? $"custom:{pluginConfig.Variant}"
                    : pluginConfig.Name.ToId();
                if (loggedPlugins.Add(pluginId))
                    startupLogger.LogInformation(
                        "Registered plugin '{PluginId}' implementation {PluginType} from {Assembly} ({Source})",
                        pluginId, plugin.GetType().FullName,
                        plugin.GetType().Assembly.GetName().Name,
                        PluginSource(plugin, services.GetRequiredService<IOptions<GatewayOptions>>().Value.PluginsPath));

                try { plugin.ValidateConfig(pluginConfig.Config); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Route '{route.Id}': invalid config for plugin '{pluginConfig.Name}': {ex.Message}", ex);
                }
            }
        }
    }

    private static readonly HashSet<System.Reflection.Assembly> BuiltInPluginAssemblies =
    [
        typeof(Security.ApiKey.ApiKeyAuthPlugin).Assembly,
        typeof(Traffic.RateLimiting.RateLimitPlugin).Assembly,
        typeof(Transformation.Plugins.HeaderTransformPlugin).Assembly,
    ];

    /// <summary>Where the winning implementation came from: built-in, plugins-folder DLL, or host DI.</summary>
    private static string PluginSource(IPipelinePlugin plugin, string? pluginsPath)
    {
        var assembly = plugin.GetType().Assembly;
        if (!string.IsNullOrWhiteSpace(pluginsPath) && !string.IsNullOrEmpty(assembly.Location) &&
            assembly.Location.StartsWith(Path.GetFullPath(pluginsPath), StringComparison.OrdinalIgnoreCase))
            return "plugins-folder";
        return BuiltInPluginAssemblies.Contains(assembly) ? "built-in" : "host-di";
    }

    private static IPipelinePlugin? ResolvePlugin(IEnumerable<IPipelinePlugin> plugins, PluginConfig config) =>
        plugins.LastOrDefault(p =>
            p.Id == config.Name.ToString().ToLowerInvariant()
            || (p.Name == config.Name && p.Variant == config.Variant));

    private static RequestDelegate BuildRouteChain(
        WebApplication app, GatewayRoute route, GatewayOptions gatewayOptions)
    {
        var chain = ((IApplicationBuilder)app).New();

        chain.Use(async (context, next) =>
        {
            context.Items[GatewayItems.RouteId] = route.Id;
            Activity.Current?.SetTag("conduitsharp.route_id", route.Id);

            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ConduitSharp.Gateway.Pipeline")
                    .LogError(ex, "Unhandled exception in plugin pipeline.");

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync("Internal Server Error");
                }
            }
        });

        var registeredPlugins = app.Services.GetServices<IPipelinePlugin>().ToList();
        var pluginChain = route.Plugins
            .Where(p => p.Enabled && p.Name != PluginName.HttpProxy)
            .OrderBy(p => p.Order)
            .Select(config => (config, plugin: ResolvePlugin(registeredPlugins, config)
                ?? throw new InvalidOperationException(
                    $"Route '{route.Id}': no plugin registered for '{config.Name}'.")))
            .ToList();

        var readsBody = pluginChain.Any(p => p.plugin.ReadsRequestBody);
        var canRetry  = route.Cluster is not null && route.Retry is { MaxAttempts: > 1 };

        var captureBytes = pluginChain.Sum(p => (long)p.plugin.CaptureMemoryBytes(p.config.Config));

        if (captureBytes > 0)
        {
            chain.Use(async (context, next) =>
            {
                SetMaxRequestBodySize(context, route, gatewayOptions);

                var budget = context.RequestServices.GetRequiredService<Middleware.RequestBodyBudget>();
                if (!budget.TryReserveRam(captureBytes))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsync("The gateway is at capacity capturing request bodies. Retry shortly.");
                    return;
                }

                try
                {
                    await next(context);
                }
                finally
                {
                    budget.ReleaseRam(captureBytes);
                }
            });
        }

        if (route.StreamOnly || (!readsBody && !canRetry))
        {
            chain.Use(async (context, next) =>
            {
                SetMaxRequestBodySize(context, route, gatewayOptions);
                await next(context);
            });
        }
        else
        {
            chain.Use(BufferRequestBody(route, gatewayOptions, readsBody));
        }

        foreach (var (config, plugin) in pluginChain)
        {
            chain.Use(async (context, next) =>
            {
                using var activity = PipelineTelemetry.ActivitySource.StartActivity($"plugin.{config.Name}");
                activity?.SetTag("conduitsharp.plugin", config.Name.ToString());

                try
                {
                    await plugin.ExecuteAsync(context, config.Config, next);
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
            });
        }

        if (route.Cluster is not null)
        {
            chain.Run(context => ((RequestDelegate)context.Items[GatewayItems.ProxyNext]!)(context));
        }
        else
        {
            chain.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsync("Route has no upstream configured.");
            });
        }

        return chain.Build();
    }

    /// <summary>Both paths delegate the per-route size limit to the server (Kestrel → 413); the buffered path's own loop check remains the backstop for chunked bodies.</summary>
    private static void SetMaxRequestBodySize(HttpContext context, GatewayRoute route, GatewayOptions gatewayOptions)
    {
        var maxBodyBytes = route.MaxRequestBodyBytes ?? gatewayOptions.RequestLimits.MaxRequestBodyBytes;
        if (maxBodyBytes < 0) return;
        var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = maxBodyBytes == 0 ? null : maxBodyBytes;
    }

    private static Func<HttpContext, RequestDelegate, Task> BufferRequestBody(
        GatewayRoute route, GatewayOptions gatewayOptions, bool readsBody) => async (context, next) =>
    {
        if (!readsBody
            && !Proxy.UpstreamRetry.IsIdempotent(context.Request.Method)
            && route.Retry is not { MaxAttempts: > 1, RetryNonIdempotent: true })
        {
            SetMaxRequestBodySize(context, route, gatewayOptions);
            await next(context);
            return;
        }

        var budget = context.RequestServices.GetRequiredService<Middleware.RequestBodyBudget>();
        var maxBodyBytes = route.MaxRequestBodyBytes ?? gatewayOptions.RequestLimits.MaxRequestBodyBytes;

        SetMaxRequestBodySize(context, route, gatewayOptions);

        if (context.Request.ContentLength == 0 && context.Request.Headers.TransferEncoding.Count == 0)
        {
            await next(context);
            return;
        }

        if (maxBodyBytes > 0 && context.Request.ContentLength > maxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsync("Request body exceeds the maximum allowed size.");
            return;
        }
        var largestBudget = Math.Max(budget.MaxRamBytes, budget.MaxDiskBytes);
        if (largestBudget > 0 && context.Request.ContentLength > largestBudget)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("The gateway is at capacity buffering request bodies. Retry shortly.");
            return;
        }

        var configuredThreshold = Math.Clamp(
            gatewayOptions.RequestLimits.RamBufferThresholdBytes, 4 * 1024, int.MaxValue);

        var tooBigForMemory = context.Request.ContentLength > configuredThreshold;

        var memoryThreshold = tooBigForMemory
            ? 0
            : (int)Math.Min(configuredThreshold, budget.RamHeadroom);

        long ramReserved = 0;
        if (memoryThreshold >= 4 * 1024 && budget.TryReserveRam(memoryThreshold))
            ramReserved = memoryThreshold;
        else
            memoryThreshold = 0;

        var spillDirectory = string.IsNullOrWhiteSpace(gatewayOptions.RequestLimits.SpillDirectory)
            ? Path.GetTempPath()
            : gatewayOptions.RequestLimits.SpillDirectory;

        var buffered = new Microsoft.AspNetCore.WebUtilities.FileBufferingReadStream(
            context.Request.Body, memoryThreshold, bufferLimit: null, spillDirectory);
        var scratch  = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);
        long diskReserved = 0;
        var  spilled      = false;
        try
        {
            long total = 0;
            int read;
            while ((read = await buffered.ReadAsync(scratch)) > 0)
            {
                if (maxBodyBytes > 0 && total + read > maxBodyBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    await context.Response.WriteAsync("Request body exceeds the maximum allowed size.");
                    return;
                }

                total += read;

                if (!spilled && !buffered.InMemory)
                {
                    if (!budget.TryReserveDisk(total))
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsync("The gateway is at capacity buffering request bodies. Retry shortly.");
                        return;
                    }

                    spilled       = true;
                    diskReserved  = total;
                    budget.ReleaseRam(ramReserved);
                    ramReserved   = 0;
                }
                else if (spilled)
                {
                    if (!budget.TryReserveDisk(read))
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsync("The gateway is at capacity buffering request bodies. Retry shortly.");
                        return;
                    }

                    diskReserved += read;
                }
            }

            buffered.Position = 0;
            context.Request.Body = new NonDisposableStream(buffered);

            await next(context);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(scratch);
            budget.ReleaseDisk(diskReserved);
            budget.ReleaseRam(ramReserved);
            await buffered.DisposeAsync();
        }
    };

#pragma warning disable CA2213
    private sealed class NonDisposableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { }
    }
#pragma warning restore CA2213

    private static void MapAdminApi(WebApplication app, string? adminKeyHash, string routesPath)
    {
        if (string.IsNullOrWhiteSpace(adminKeyHash))
            return;

        byte[] expectedKeyHash;
        try
        {
            expectedKeyHash = Convert.FromHexString(adminKeyHash);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Gateway:AdminKeyHash must be the hex-encoded SHA-256 of the admin key.", ex);
        }

        if (expectedKeyHash.Length != SHA256.HashSizeInBytes)
            throw new InvalidOperationException(
                $"Gateway:AdminKeyHash must be a SHA-256 hash ({SHA256.HashSizeInBytes * 2} hex chars); " +
                $"got {expectedKeyHash.Length * 2}.");

        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/admin"))
            {
                await next(ctx);
                return;
            }

            var authorized = ctx.Request.Headers.TryGetValue("X-Admin-Key", out var key)
                && CryptographicOperations.FixedTimeEquals(
                       SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.ToString())),
                       expectedKeyHash);

            if (!authorized)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Unauthorized.");
                return;
            }

            if (ctx.Request.Method == "DELETE"
                && ctx.Request.Path.StartsWithSegments("/admin/cache", out var rest)
                && rest.HasValue && rest.Value!.Trim('/') is { Length: > 0 } routeId)
            {
                var cache   = ctx.RequestServices.GetRequiredService<ICacheService>();
                var removed = await cache.RemoveByPrefixAsync(routeId + '\0', ctx.RequestAborted);
                ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ConduitSharp.Gateway.Admin")
                    .LogInformation("Admin cache invalidation: {Count} entries for route '{RouteId}' from {RemoteIp}.",
                        removed, routeId, ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                await ctx.Response.WriteAsync($"Invalidated {removed} cache entr{(removed == 1 ? "y" : "ies")} for route '{routeId}'.");
                return;
            }

            if (ctx.Request.Method != "POST"
                || !ctx.Request.Path.StartsWithSegments("/admin/routes/reload"))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsync("Unknown admin endpoint.");
                return;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.Body))
                body = await reader.ReadToEndAsync();

            GatewayRoutesConfiguration parsedRoutes;
            try
            {
                parsedRoutes = GatewayRoutesConfiguration.Parse(body);
                parsedRoutes.Validate();
                ValidateRouteTable(ctx.RequestServices, parsedRoutes);
            }
            catch (Exception ex)
            {
                ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ConduitSharp.Gateway.Admin")
                    .LogError(ex, "Admin route reload rejected: {Reason}", ex.Message);
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync($"Invalid routes configuration: {ex.Message}");
                return;
            }

            var tempPath = routesPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(tempPath, body);
                File.Move(tempPath, routesPath, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }

            var reloadLogger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ConduitSharp.Gateway.Admin");
            var remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            reloadLogger.LogInformation(
                "Admin route reload applied: {RouteCount} routes from {RemoteIp}.",
                parsedRoutes.Routes.Count, remoteIp);
            GatewayTelemetry.AdminReloadCounter.Add(1);
            Activity.Current?.AddEvent(new ActivityEvent("admin.routes.reloaded",
                tags: new ActivityTagsCollection
                {
                    ["conduitsharp.route_count"] = parsedRoutes.Routes.Count,
                    ["client.address"]           = remoteIp,
                }));

            ctx.RequestServices.GetRequiredService<Proxy.GatewayRouteTable>().Load(parsedRoutes);
            ctx.RequestServices.GetRequiredService<GatewayRoutesConfiguration>()
                .ReplaceRoutes(parsedRoutes.Routes);

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await ctx.Response.WriteAsync("Routes reloaded.");
        });
    }

    private static void MapHealthEndpoints(WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path == "/healthz")
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                await ctx.Response.WriteAsync("OK");
                return;
            }

            if (ctx.Request.Path == "/readyz")
            {
                var routes = ctx.RequestServices.GetRequiredService<GatewayRoutesConfiguration>();
                var ready  = routes.Routes.Count > 0;
                ctx.Response.StatusCode = ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsync(ready ? "Ready" : "Not ready");
                return;
            }

            await next(ctx);
        });
    }
}

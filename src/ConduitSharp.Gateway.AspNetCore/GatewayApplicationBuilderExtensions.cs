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

        if (options.EnableAdminApi)
        {
            var routesPath = options.RoutesPath ?? gatewayOptions.RoutesPath;
            MapAdminApi(app, gatewayOptions.AdminKeyHash, routesPath);
        }

        if (options.MapHealthEndpoints)
            MapHealthEndpoints(app);

        // Aggregated Swagger UI is an optional add-on (ConduitSharp.Gateway.AspNetCore.Swagger):
        // call app.UseConduitSharpGatewaySwagger() before this method to enable it.

        // One gateway.request span per request the gateway handles — including the ones that match
        // no route, so a 404 is still traceable. Sits after admin/health, which answer and return
        // before reaching it. The route id is tagged on later, from inside the matched route's chain.
        //
        // The same finally notifies every IRequestObserver (structured request log, OTel request
        // counter/duration/error metrics) — this is the only place per-request observability
        // fan-out happens, so it must sit on the outermost path where timing covers everything.
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

                    // An observer must never be able to fail the request from inside a finally.
                    foreach (var observer in observers)
                    {
                        try { observer.OnRequestCompleted(observation); }
                        catch { /* observers are fire-and-forget by contract */ }
                    }
                }
            }
        });

        ValidateRouteTable(app.Services, gatewayRoutes);

        // The route table owns everything a hot reload must swap together: one compiled
        // RequestDelegate per route (plugins resolved once, not per request), the route lookup,
        // plugin-only endpoints, retry pipelines, and YARP's in-memory config.
        var table = app.Services.GetRequiredService<GatewayRouteTable>();
        table.Initialize(route => BuildRouteChain(app, route, gatewayOptions));
        table.Load(gatewayRoutes);

        app.MapReverseProxy(proxyPipeline =>
        {
            // Plugins run inside YARP's per-proxy pipeline, so the forwarder always executes within
            // their next() — a cache plugin's response tee therefore wraps the real forward, and a
            // plugin that skips next() short-circuits before YARP forwards anything.
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
        // Custom MatcherPolicies dropped into the plugins root read the route off the endpoint.
        .ConfigureEndpoints((builder, route) => builder.WithMetadata(table.RouteFor(route.RouteId)));

        // Plugin-only routes never reach YARP: it rejects a route with no cluster before any
        // middleware runs, so the table serves them as ordinary endpoints running the same chain —
        // through a mutable data source, so a hot reload can add and remove them like YARP routes.
        ((IEndpointRouteBuilder)app).DataSources.Add(table.PluginEndpoints);

        // No catch-all fallback endpoint: one would match every path and so mask endpoint routing's
        // own 405, turning "right path, wrong verb" into a 404. Unmatched requests get the
        // framework's 404 (and, under a PathPrefix, fall through to the host).
        return app;
    }

    // ---------------------------------------------------------------------------
    // Route-table validation. Runs at startup and again against the incoming table on admin
    // reload, so a reload can never swap in routes the gateway cannot serve.
    // ---------------------------------------------------------------------------
    private static void ValidateRouteTable(IServiceProvider services, GatewayRoutesConfiguration gatewayRoutes)
    {
        ValidateLoadBalancingPolicies(services, gatewayRoutes);
        ValidatePluginChains(services, gatewayRoutes);
    }

    // Every route's loadBalancingStrategy must name a registered ILoadBalancingPolicy — YARP's
    // five built-ins plus anything dropped into the plugins root. Checking against the DI set
    // rather than a hardcoded list means drop-in policies validate for free, and the error can
    // name what is actually available. YARP would catch this too, but later and less usefully.
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

    // Every enabled plugin must resolve and its config must parse, and 'http-proxy' (when named
    // explicitly) must sit last, because it is where the forward happens.
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
                // 'http-proxy' is no longer a plugin — it names the forward step itself.
                if (pluginConfig.Name == PluginName.HttpProxy) continue;

                var plugin = ResolvePlugin(allPlugins, pluginConfig)
                    ?? throw new InvalidOperationException(
                        $"Route '{route.Id}': no plugin registered for '{pluginConfig.Name}'.");

                if (route.StreamOnly && plugin.ReadsRequestBody)
                    throw new InvalidOperationException(
                        $"Route '{route.Id}': plugin '{pluginConfig.Name}' reads the request body, which requires " +
                        "the buffered body the gateway provides — it cannot run on a streamOnly route. " +
                        "Remove streamOnly from this route, or the body-reading plugin.");

                // Key by id + variant so each custom variant logs its own winner; the source tag
                // makes a silent last-registration-wins override visible at startup.
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

    // Last registration wins, so a drop-in DLL shadows the built-in of the same name.
    private static IPipelinePlugin? ResolvePlugin(IEnumerable<IPipelinePlugin> plugins, PluginConfig config) =>
        plugins.LastOrDefault(p =>
            p.Id == config.Name.ToString().ToLowerInvariant()
            || (p.Name == config.Name && p.Variant == config.Variant));

    // ---------------------------------------------------------------------------
    // Per-route chain: telemetry + error boundary → body budget → plugins → forward.
    // Compiled once at startup into a single RequestDelegate.
    // ---------------------------------------------------------------------------
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

        // Resolved once when the chain is compiled, not per request: plugins are singletons and
        // the route's plugin set is fixed until the next reload rebuilds the chain. Validation
        // already proved they resolve, so a miss here is a bug worth throwing on. Resolving
        // up front also lets the buffering decision below see ReadsRequestBody.
        var registeredPlugins = app.Services.GetServices<IPipelinePlugin>().ToList();
        var pluginChain = route.Plugins
            .Where(p => p.Enabled && p.Name != PluginName.HttpProxy)
            .OrderBy(p => p.Order)
            .Select(config => (config, plugin: ResolvePlugin(registeredPlugins, config)
                ?? throw new InvalidOperationException(
                    $"Route '{route.Id}': no plugin registered for '{config.Name}'.")))
            .ToList();

        // The buffer exists for exactly two consumers: the retry rewind and body-reading
        // plugins. A route with neither streams by definition — same path as streamOnly,
        // no config needed. (Explicit streamOnly still forces it, and is still validated
        // against body-reading plugins / retry at startup.)
        var readsBody = pluginChain.Any(p => p.plugin.ReadsRequestBody);
        var canRetry  = route.Cluster is not null && route.Retry is { MaxAttempts: > 1 };

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

    // ---------------------------------------------------------------------------
    // Buffer the request body so the retry loop can rewind it and body-reading plugins get a
    // seekable stream, enforcing the per-route size limit (413) and the gateway-wide buffering
    // budget (503).
    //
    // Buffering degrades in two tiers rather than one cliff. While the memory tier
    // (MaxMemoryBufferedBodyBytes) has headroom a body buffers in RAM, which measures ~3-5x faster
    // than spilling. Once it is full, further bodies spill to a temp file from the first byte —
    // slower, but still served — until the combined budget (MaxTotalBufferedBodyBytes) is gone and
    // the gateway sheds with a 503.
    //
    // The per-request RAM ceiling can be generous (up to 1 MiB) precisely because the memory tier
    // caps the aggregate. Above 1 MiB, FileBufferingReadStream stops renting from ArrayPool and
    // grows a bare MemoryStream by doubling — ~2x the body allocated on the LOH — which is why the
    // threshold is clamped there and not left to the operator.
    //
    // Non-idempotent methods on retry-only routes stream: the retry loop can never replay them,
    // so their buffer would have no consumer.
    // ---------------------------------------------------------------------------
    private static Func<HttpContext, RequestDelegate, Task> BufferRequestBody(
        GatewayRoute route, GatewayOptions gatewayOptions, bool readsBody) => async (context, next) =>
    {
        if (!readsBody && !Proxy.UpstreamRetry.IsIdempotent(context.Request.Method))
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
        if (budget.MaxTotalBytes > 0 && context.Request.ContentLength > budget.MaxTotalBytes)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("The gateway is at capacity buffering request bodies. Retry shortly.");
            return;
        }

        // Ask the memory tier for this body's RAM ceiling. The reservation covers the buffer's
        // capacity, not its fill: FileBufferingReadStream rents the whole threshold from ArrayPool
        // at construction and hands it back the instant it spills. A refusal is routine, not an
        // error — threshold 0 means "spill from the first byte", which is the disk tier doing its
        // job, and the request is still served.
        var configuredThreshold = Math.Clamp(gatewayOptions.RequestLimits.MemoryBufferThresholdBytes, 4 * 1024, 1024 * 1024);

        // A body Content-Length already proves cannot fit the RAM tier gains nothing from a buffer:
        // it would rent the threshold, fill it, then copy every one of those bytes to disk anyway.
        // Spilling from the first byte skips that copy and leaves the tier for bodies that can
        // actually be served out of it. Chunked bodies have no Content-Length, so they still try —
        // being wrong there just costs the copy this branch avoids.
        var tooBigForMemory = context.Request.ContentLength > configuredThreshold;

        var memoryThreshold = tooBigForMemory
            ? 0
            : (int)Math.Min(configuredThreshold, budget.MemoryHeadroom);

        long memoryReserved = 0;
        if (memoryThreshold >= 4 * 1024 && budget.TryReserveMemory(memoryThreshold))
            memoryReserved = memoryThreshold;
        else
            memoryThreshold = 0; // known too big, no headroom, or the tier is off — spill this one

        var spillDirectory = string.IsNullOrWhiteSpace(gatewayOptions.RequestLimits.SpillDirectory)
            ? Path.GetTempPath()
            : gatewayOptions.RequestLimits.SpillDirectory;

        var buffered = new Microsoft.AspNetCore.WebUtilities.FileBufferingReadStream(
            context.Request.Body, memoryThreshold, bufferLimit: null, spillDirectory);
        var scratch  = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);
        long reserved = 0;
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

                // The budget bounds bytes buffered concurrently gateway-wide (memory + spill),
                // keeping the 503 load-shed behavior as the backstop.
                if (!budget.TryReserve(read))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsync("The gateway is at capacity buffering request bodies. Retry shortly.");
                    return;
                }

                reserved += read;
                total += read;

                // The body outgrew its RAM ceiling: FileBufferingReadStream has copied everything to
                // the temp file and returned the rented buffer to the pool, so that RAM is genuinely
                // free — hand it back to the tier so the next request can use it. The bytes stay
                // counted against the total; only the tier holding them changed.
                if (memoryReserved > 0 && !buffered.InMemory)
                {
                    budget.ReleaseMemory(memoryReserved);
                    memoryReserved = 0;
                }
            }

            buffered.Position = 0;
            context.Request.Body = new NonDisposableStream(buffered);

            await next(context);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(scratch);
            budget.Release(reserved);
            budget.ReleaseMemory(memoryReserved); // no-op if the spill already handed it back
            await buffered.DisposeAsync(); // deletes the spill file, if any
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

    // ---------------------------------------------------------------------------
    // Admin API — POST /admin/routes/reload, DELETE /admin/cache/{routeId}
    // Registered as Use() middleware BEFORE the gateway's endpoints.
    // Disabled entirely when Gateway:AdminKeyHash is null or empty.
    // The incoming X-Admin-Key header value is SHA-256 hashed before comparison —
    // the raw secret is never stored in config.
    // ---------------------------------------------------------------------------
    private static void MapAdminApi(WebApplication app, string? adminKeyHash, string routesPath)
    {
        if (string.IsNullOrWhiteSpace(adminKeyHash))
            return;

        // Decoded once at startup: a malformed Gateway:AdminKeyHash should fail the gateway, not
        // every admin request. FixedTimeEquals also needs the raw bytes, not the hex text.
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

            // FixedTimeEquals, not string.Equals: this is the only endpoint that takes a secret,
            // and an ordinary comparison returns as soon as two bytes differ. That timing signal
            // is enough to recover the expected hash byte by byte over many requests.
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

            // DELETE /admin/cache/{routeId} — flush a route's cached responses.
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

            // POST /admin/routes/reload — everything below handles the reload.
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
                // Same gate as startup: reject a table naming an unregistered load-balancing
                // policy or plugin, or a plugin config that does not parse — nothing is swapped
                // on failure.
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

            // Atomic swap (O4): write to a temp file in the same directory, then rename over
            // the target. routes.json is therefore never left partially written — a crash
            // mid-write leaves the old, valid file intact rather than corrupt config.
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

            // Audit trail (O5): structured log + counter + span event, so who reloaded what
            // and when is observable.
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

            // Hot swap: compile chains and rebuild YARP's route/cluster config in place — no host
            // restart, in-flight requests unaffected. The shared GatewayRoutesConfiguration list
            // is refreshed so /readyz and other readers see the new table.
            ctx.RequestServices.GetRequiredService<Proxy.GatewayRouteTable>().Load(parsedRoutes);
            ctx.RequestServices.GetRequiredService<GatewayRoutesConfiguration>()
                .ReplaceRoutes(parsedRoutes.Routes);

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await ctx.Response.WriteAsync("Routes reloaded.");
        });
    }

    // ---------------------------------------------------------------------------
    // Gateway-owned health endpoints (answered by the gateway itself, never proxied):
    //   /healthz — liveness: the process is up.
    //   /readyz  — readiness: a route table is loaded and the gateway can serve.
    // Deliberately independent of upstream reachability — a downstream blip must not
    // pull every gateway replica out of rotation (correlated-failure anti-pattern).
    // ---------------------------------------------------------------------------
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

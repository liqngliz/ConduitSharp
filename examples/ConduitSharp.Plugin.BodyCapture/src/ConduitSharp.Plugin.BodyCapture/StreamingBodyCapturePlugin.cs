using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConduitSharp.Plugin.BodyCapture;

/// <summary>
/// Logs a bounded prefix of the request body <em>without buffering it</em>, by running the
/// framework's own HttpLogging middleware over the forward.
///
/// Where <see cref="BodyCapturePlugin"/> declares <see cref="IPipelinePlugin.ReadsRequestBody"/> and
/// so forces the gateway to buffer the whole body (memory + temp-file spill) just to hand it a
/// rewindable stream, this plugin leaves the route on the streaming path. HttpLogging wraps
/// <see cref="HttpRequest.Body"/> in its internal <c>RequestBufferingStream</c>, which tees the first
/// <c>maxSize</c> bytes into pooled segments as YARP streams them upstream and drops the rest. Heap
/// cost is the prefix, never the body — so this runs on <c>streamOnly</c> routes too.
///
/// Captured bodies are emitted through the host's <see cref="ILoggerFactory"/>, so they leave the
/// process through whatever it has wired up — including the gateway's OpenTelemetry logger provider
/// (→ OTLP → Loki) — under this plugin's own category rather than HttpLogging's.
/// </summary>
public sealed class StreamingBodyCapturePlugin : IPipelinePlugin
{
    private const int DefaultMaxSize = 4 * 1024;

    /// <summary>
    /// Hard ceiling on a route's <c>maxSize</c>. Capture memory sits on the streaming path, which
    /// never reserves against <c>Gateway:RequestLimits:MaxTotalBufferedBodyBytes</c> — so nothing
    /// downstream sheds load if it grows. 32 KiB keeps a captured prefix well under the 85 KiB large
    /// object heap threshold (and matches HttpLogging's own RequestBodyLogLimit default), so capture
    /// stays on pooled, gen-0-sized buffers no matter how many requests are in flight.
    /// </summary>
    private const int MaxAllowedSize = 32 * 1024;

    internal const string LimitKey = "conduitsharp.body-capture.limit";
    private  const string NextKey  = "conduitsharp.body-capture.next";

    private readonly RequestDelegate _pipeline;

    public StreamingBodyCapturePlugin(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // HttpLoggingMiddleware is internal, reachable only via UseHttpLogging() on a pipeline we
        // own, and it resolves its options from DI. The gateway hands drop-in plugins no seam onto
        // the host's IServiceCollection, so the middleware's options live in a container of our own
        // — wired to the host's ILoggerFactory, which is what keeps the captured bodies flowing out
        // through the host's OpenTelemetry logger provider rather than into a private void.
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new BodyCaptureLoggerFactory(loggerFactory));
        services.AddLogging(); // TryAdds ILoggerFactory, so the line above wins
        services.AddHttpLogging(options =>
        {
            options.LoggingFields = HttpLoggingFields.RequestBody;
            options.CombineLogs   = true;
        });
        services.AddHttpLoggingInterceptor<PerRouteBodyLimitInterceptor>();

        // Built once — the plugin is a singleton and the middleware is stateless per request. The
        // terminal hands control back to the gateway's chain through Items, the same handoff the
        // gateway itself uses for YARP's forward (GatewayItems.ProxyNext).
        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UseHttpLogging();
        app.Run(context => ((RequestDelegate)context.Items[NextKey]!)(context));
        _pipeline = app.Build();
    }

    public PluginName Name => PluginName.Custom;
    public string? Variant => "body-capture-streaming";
    public string Id => "body-capture-streaming";

    // Deliberately false: we do NOT need the gateway's buffered, rewindable body. HttpLogging
    // observes the bytes as YARP streams them, so the route stays on the zero-copy streaming path.
    public bool ReadsRequestBody => false;

    public void ValidateConfig(JsonElement config)
    {
        if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("maxSize", out var maxSizeProp))
        {
            if (maxSizeProp.ValueKind != JsonValueKind.Number || !maxSizeProp.TryGetInt32(out var maxSize) || maxSize <= 0)
            {
                throw new InvalidOperationException("Plugin 'body-capture-streaming' config error: 'maxSize' must be a positive integer.");
            }

            if (maxSize > MaxAllowedSize)
            {
                throw new InvalidOperationException(
                    $"Plugin 'body-capture-streaming' config error: 'maxSize' must not exceed {MaxAllowedSize} bytes (32 KiB); got {maxSize}. " +
                    "Capture memory sits on the streaming path, where it is not counted against " +
                    "Gateway:RequestLimits:MaxTotalBufferedBodyBytes and cannot shed load — and a larger prefix would " +
                    "put every captured body on the large object heap. Log a bounded prefix here and send full bodies " +
                    "to an audit sink instead.");
            }
        }
    }

    public Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Items[NextKey]  = next;
        context.Items[LimitKey] = MaxSize(config);
        return _pipeline(context);
    }

    // Clamped as well as validated: ValidateConfig already rejects an oversized maxSize at startup
    // and on reload, so this only matters if a plugin host ever skips it — but the ceiling exists to
    // bound heap, and a bound that depends on someone else remembering to call a validator is not one.
    private static int MaxSize(JsonElement config) =>
        config.ValueKind == JsonValueKind.Object
        && config.TryGetProperty("maxSize", out var maxSizeProp)
        && maxSizeProp.TryGetInt32(out var parsed)
        && parsed > 0
            ? Math.Min(parsed, MaxAllowedSize)
            : DefaultMaxSize;

    /// <summary>
    /// Applies the route's <c>maxSize</c> to the request HttpLogging is about to capture, and stamps
    /// the matched route id + path into the same combined log record — without these the record is
    /// a bare body with nothing tying it to the route that produced it.
    /// HttpLoggingOptions is process-wide, but the limit is per-route config, and the interceptor is
    /// the framework's supported seam for varying it per request.
    /// </summary>
    private sealed class PerRouteBodyLimitInterceptor : IHttpLoggingInterceptor
    {
        public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logging)
        {
            if (logging.HttpContext.Items.TryGetValue(LimitKey, out var value) && value is int limit)
                logging.RequestBodyLogLimit = limit;

            // Same key the gateway sets per request (GatewayItems.RouteId — internal, so by value).
            if (logging.HttpContext.Items.TryGetValue("ConduitSharp.RouteId", out var routeId) && routeId is string id)
                logging.AddParameter("conduitsharp.route_id", id);
            logging.AddParameter("conduitsharp.path", logging.HttpContext.Request.Path.Value ?? "");

            return default;
        }

        // Request bodies only — the response never enters the picture, so nothing to adjust.
        public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logging) => default;
    }

    /// <summary>
    /// Re-homes HttpLogging's loggers under this plugin's category. HttpLogging logs captured
    /// bodies at Information under <c>Microsoft.AspNetCore.HttpLogging.*</c>, and every stock
    /// ASP.NET Core log config filters <c>Microsoft.AspNetCore</c> to Warning — which would swallow
    /// every body silently, with the plugin still looking healthy. Renaming the category makes the
    /// capture obey this plugin's log level instead of the framework's, and tags the records in
    /// Loki as body-capture rather than burying them in framework noise.
    /// </summary>
    private sealed class BodyCaptureLoggerFactory(ILoggerFactory inner) : ILoggerFactory
    {
        private static readonly string Category = typeof(StreamingBodyCapturePlugin).FullName!;

        public ILogger CreateLogger(string categoryName) =>
            inner.CreateLogger(categoryName.StartsWith("Microsoft.AspNetCore.HttpLogging", StringComparison.Ordinal)
                ? Category
                : categoryName);

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        // The host owns the wrapped factory's lifetime — this is a view onto it, not a handle.
        public void Dispose() { }
    }
}

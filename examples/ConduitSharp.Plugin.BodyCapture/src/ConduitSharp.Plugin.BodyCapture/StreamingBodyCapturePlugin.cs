using System.Buffers;
using System.Text;
using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConduitSharp.Plugin.BodyCapture;

/// <summary>
/// Logs a bounded prefix of the request body, picking the cheapest path per request:
///
/// <list type="bullet">
/// <item>If the gateway already buffered the body — a retry route, or a body-reading plugin on the
/// same route — <see cref="HttpRequest.Body"/> is seekable, so this reads the prefix directly off
/// that existing buffer and rewinds. No second copy: on a retry route capture is near-free, and it
/// reads raw bytes so it captures binary bodies too.</item>
/// <item>Otherwise the route is streaming, so it runs the framework's HttpLogging middleware over the
/// forward. HttpLogging wraps the body in its internal <c>RequestBufferingStream</c>, teeing the first
/// <c>maxSize</c> bytes into pooled segments as YARP streams them upstream and dropping the rest. Heap
/// cost is the prefix, never the body — so this runs on <c>streamOnly</c> routes too. (HttpLogging
/// only captures text-ish media types; a binary body on a pure streaming route is not logged.)</item>
/// </list>
///
/// <see cref="IPipelinePlugin.ReadsRequestBody"/> stays <c>false</c> — this never forces the gateway to
/// buffer. It only reuses a buffer that already exists. This supersedes the old <c>BodyCapturePlugin</c>,
/// which declared <c>ReadsRequestBody</c> and so forced whole-body buffering on every route.
///
/// Captured bodies are emitted through the host's <see cref="ILoggerFactory"/>, so they leave the
/// process through whatever it has wired up — including the gateway's OpenTelemetry logger provider
/// (→ OTLP → Loki) — under this plugin's own category rather than HttpLogging's.
/// </summary>
public sealed class StreamingBodyCapturePlugin : IPipelinePlugin
{
    private const int DefaultMaxSize = 4 * 1024;

    /// <summary>
    /// Default ceiling on a route's <c>maxSize</c>. 32 KiB keeps a captured prefix well under the
    /// 85 KiB large object heap threshold (and matches HttpLogging's own RequestBodyLogLimit default),
    /// so capture stays on pooled, gen-0-sized buffers no matter how many requests are in flight.
    /// </summary>
    private const int DefaultMaxAllowedSize = 32 * 1024;

    /// <summary>
    /// Effective ceiling on a route's <c>maxSize</c>, from <c>BodyCapture:MaxCaptureBytes</c>
    /// (default 32 KiB, floored at the per-route default of 4 KiB). Raising it past 85 KiB puts every
    /// captured prefix on the large object heap — and capture on the streaming path is now counted
    /// against <c>Gateway:RequestLimits:MaxRamBufferedBodyBytes</c> (see <see cref="CaptureMemoryBytes"/>),
    /// so a larger prefix reserves more budget and sheds sooner, rather than growing unchecked.
    /// </summary>
    private readonly int _maxAllowedSize;

    internal const string LimitKey = "conduitsharp.body-capture.limit";
    private  const string NextKey  = "conduitsharp.body-capture.next";

    private readonly RequestDelegate _pipeline;
    // Reuse-branch records log here; the tee branch re-homes HttpLogging's records to the same
    // category (see BodyCaptureLoggerFactory), so both paths surface under one name.
    private readonly ILogger _logger;

    public StreamingBodyCapturePlugin(ILoggerFactory loggerFactory, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger(typeof(StreamingBodyCapturePlugin).FullName!);

        // Read as long so an oversized value reports what it is rather than failing as an opaque
        // int conversion error. HttpLogging's RequestBodyLogLimit is an int, so that is the hard cap.
        var requestedCeiling = configuration?.GetValue<long?>("BodyCapture:MaxCaptureBytes") ?? DefaultMaxAllowedSize;
        if (requestedCeiling > int.MaxValue)
            throw new InvalidOperationException(
                $"Plugin 'body-capture' config error: 'BodyCapture:MaxCaptureBytes' of {Mb(requestedCeiling)} exceeds the " +
                $"{Mb(int.MaxValue)} maximum a captured prefix can address. Send full bodies to a file sink " +
                "(body-capture-file) instead of holding them in memory.");

        _maxAllowedSize = (int)Math.Max(requestedCeiling, DefaultMaxSize);

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

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:F1} MiB";

    public PluginName Name => PluginName.Custom;
    public string? Variant => "body-capture";
    public string Id => "body-capture";

    // Deliberately false: we do NOT need the gateway's buffered, rewindable body. HttpLogging
    // observes the bytes as YARP streams them, so the route stays on the zero-copy streaming path.
    public bool ReadsRequestBody => false;

    /// <summary>
    /// The captured prefix is bounded per request, but it multiplies by concurrency — and sitting on
    /// the streaming path it was invisible to the RAM budget, so nothing shed load
    /// as it grew. Declaring it here puts the tee under the same ceiling and the same 503 as a
    /// buffered body: a connection flood now sheds instead of climbing unchecked.
    /// </summary>
    public int CaptureMemoryBytes(JsonElement config) => MaxSize(config);

    public void ValidateConfig(JsonElement config)
    {
        if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("maxSize", out var maxSizeProp))
        {
            if (maxSizeProp.ValueKind != JsonValueKind.Number || !maxSizeProp.TryGetInt32(out var maxSize) || maxSize <= 0)
            {
                throw new InvalidOperationException("Plugin 'body-capture' config error: 'maxSize' must be a positive integer.");
            }

            if (maxSize > _maxAllowedSize)
            {
                throw new InvalidOperationException(
                    $"Plugin 'body-capture' config error: 'maxSize' must not exceed {_maxAllowedSize} bytes; got {maxSize}. " +
                    "A captured prefix is reserved against Gateway:RequestLimits:MaxRamBufferedBodyBytes, so a larger " +
                    "prefix consumes that budget faster and sheds (503) sooner — and past 85 KiB it puts every captured " +
                    "body on the large object heap. Raise the ceiling with BodyCapture:MaxCaptureBytes only alongside a " +
                    "budget sized to real available memory; otherwise log a bounded prefix here and send full bodies to " +
                    "an audit sink instead.");
            }
        }
    }

    public Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Body already buffered (retry route / a body-reading plugin on the route): read the prefix
        // off the existing seekable buffer rather than teeing a second copy. On a retry route this is
        // the cheap path, and it captures binary bodies HttpLogging would skip.
        if (context.Request.Body.CanSeek)
            return CaptureFromBufferedBodyAsync(context, MaxSize(config), next);

        context.Items[NextKey]  = next;
        context.Items[LimitKey] = MaxSize(config);
        return _pipeline(context);
    }

    // Reads up to maxSize off the already-buffered, seekable body, rewinds, logs the prefix, forwards.
    // Same rewind discipline the retry loop relies on: Position is left at 0 for the forward and any
    // replay. No EnableBuffering — the gateway's buffer is reused in place, staying under its budget.
    private async Task CaptureFromBufferedBodyAsync(HttpContext context, int maxSize, RequestDelegate next)
    {
        context.Request.Body.Position = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(maxSize);
        try
        {
            var read = await context.Request.Body.ReadAtLeastAsync(
                buffer.AsMemory(0, maxSize), maxSize, throwOnEndOfStream: false);

            var truncated = false;
            if (read == maxSize)
            {
                var extra = new byte[1];
                truncated = await context.Request.Body.ReadAsync(extra.AsMemory(0, 1)) > 0;
            }
            context.Request.Body.Position = 0;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                var body = Encoding.UTF8.GetString(buffer, 0, read);
                if (truncated) body += "... (truncated)";
                var routeId = context.Items.TryGetValue("ConduitSharp.RouteId", out var r) && r is string id ? id : "";
                _logger.LogInformation(
                    "Captured request body for path {Path} route {RouteId}: {Body}",
                    context.Request.Path.Value ?? "", routeId, body);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await next(context);
    }

    // Clamped as well as validated: ValidateConfig already rejects an oversized maxSize at startup
    // and on reload, so this only matters if a plugin host ever skips it — but the ceiling exists to
    // bound heap, and a bound that depends on someone else remembering to call a validator is not one.
    private int MaxSize(JsonElement config) =>
        config.ValueKind == JsonValueKind.Object
        && config.TryGetProperty("maxSize", out var maxSizeProp)
        && maxSizeProp.TryGetInt32(out var parsed)
        && parsed > 0
            ? Math.Min(parsed, _maxAllowedSize)
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

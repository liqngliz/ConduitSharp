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
/// Logs a bounded prefix of the request body, the response body, or both, chosen per direction by
/// config:
///
/// <code>
/// { "request": { "maxSize": 4096 }, "response": { "maxSize": 8192 } }
/// </code>
///
/// A direction is captured only if its block is present with a positive <c>maxSize</c>. Omit the
/// block (or set <c>maxSize: 0</c>) to skip that direction.
///
/// <b>Request</b> capture picks the cheaper path per request. If the gateway already buffered the body
/// (a retry route, or a body-reading plugin), <see cref="HttpRequest.Body"/> is seekable and the prefix
/// is read straight off it (raw bytes, so binary is captured too). Otherwise the route is streaming and
/// the framework's HttpLogging middleware tees the first <c>maxSize</c> bytes as YARP streams them
/// upstream (text-ish media types only). Either way <see cref="IPipelinePlugin.ReadsRequestBody"/>
/// stays <c>false</c> — capture never forces the gateway to buffer.
///
/// <b>Response</b> capture wraps <see cref="HttpResponse.Body"/> in a bounded write-through tee that
/// copies the first <c>maxSize</c> bytes as the response streams to the client. It runs on both request
/// paths, so a retry route captures the response too, and it reads raw bytes so binary responses are
/// captured.
///
/// Both prefixes are RAM-only (never spill) and are declared through <see cref="CaptureMemoryBytes"/>,
/// so the gateway reserves <c>requestMaxSize + responseMaxSize</c> against
/// <c>Gateway:RequestLimits:MaxRamBufferedBodyBytes</c> and sheds with a 503 under a flood rather than
/// growing unchecked. Captured bodies are emitted through the host's <see cref="ILoggerFactory"/> under
/// this plugin's category.
/// </summary>
public sealed class StreamingBodyCapturePlugin : IPipelinePlugin
{
    private const int DefaultMaxSize = 4 * 1024;

    /// <summary>
    /// Default per-direction ceiling on <c>maxSize</c>. 32 KiB keeps a captured prefix well under the
    /// 85 KiB large object heap threshold (and matches HttpLogging's own RequestBodyLogLimit default),
    /// so capture stays on pooled, gen-0-sized buffers no matter how many requests are in flight.
    /// </summary>
    private const int DefaultMaxAllowedSize = 32 * 1024;

    /// <summary>
    /// Effective per-direction ceiling on <c>maxSize</c>, from <c>BodyCapture:MaxCaptureBytes</c>
    /// (default 32 KiB, floored at 4 KiB). Applies to each direction independently, so a route
    /// capturing both reserves up to twice this against
    /// <c>Gateway:RequestLimits:MaxRamBufferedBodyBytes</c>.
    /// </summary>
    private readonly int _maxAllowedSize;

    internal const string LimitKey = "conduitsharp.body-capture.limit";
    private  const string NextKey  = "conduitsharp.body-capture.next";

    private readonly RequestDelegate _pipeline;
    // Reuse-branch and response records log here; the streaming request tee re-homes HttpLogging's
    // records to the same category (see BodyCaptureLoggerFactory), so every path surfaces under one name.
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
        //
        // Request body only: the response is captured by our own write-through tee, which also covers
        // the retry/reuse routes HttpLogging never sees.
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

    // Deliberately false: we do NOT need the gateway's buffered, rewindable body. HttpLogging observes
    // the request bytes as YARP streams them, and the response tee observes them as YARP writes them.
    public bool ReadsRequestBody => false;

    /// <summary>
    /// RAM reserved per request: the request and response prefixes summed, because HttpLogging's
    /// combined record holds the request prefix until the response completes, so both are live at once.
    /// A direction contributes 0 when it is not captured.
    /// </summary>
    public int CaptureMemoryBytes(JsonElement config) => RequestMaxSize(config) + ResponseMaxSize(config);

    public void ValidateConfig(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object) return;

        // Catch the pre-2.0 flat shape ({ "maxSize": N } at the top level, request-only). It no longer
        // captures anything, so fail the load with a migration hint instead of a silent no-op.
        if (config.TryGetProperty("maxSize", out _))
            throw new InvalidOperationException(
                "Plugin 'body-capture' config uses the removed flat shape ({ \"maxSize\": N }). Nest it by "
                + "direction: { \"request\": { \"maxSize\": N } } to capture the request, and add a "
                + "\"response\" block to capture the response.");

        ValidateDirection(config, "request");
        ValidateDirection(config, "response");
    }

    private void ValidateDirection(JsonElement config, string direction)
    {
        if (!config.TryGetProperty(direction, out var block) || block.ValueKind != JsonValueKind.Object) return;
        if (!block.TryGetProperty("maxSize", out var maxSizeProp)) return;

        if (maxSizeProp.ValueKind != JsonValueKind.Number || !maxSizeProp.TryGetInt32(out var maxSize))
            throw new InvalidOperationException(
                $"Plugin 'body-capture' config error: '{direction}.maxSize' must be an integer.");

        if (maxSize > _maxAllowedSize)
            throw new InvalidOperationException(
                $"Plugin 'body-capture' config error: '{direction}.maxSize' must not exceed {_maxAllowedSize} bytes; got {maxSize}. " +
                "A captured prefix is reserved against Gateway:RequestLimits:MaxRamBufferedBodyBytes, so a larger prefix "
                + "consumes that budget faster and sheds (503) sooner, and past 85 KiB it puts every captured body on the "
                + "large object heap. Raise the ceiling with BodyCapture:MaxCaptureBytes only alongside a budget sized to "
                + "real available memory; otherwise send full bodies to a file sink (body-capture-file).");
    }

    public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestMax  = RequestMaxSize(config);
        var responseMax = ResponseMaxSize(config);

        // Response capture: swap Response.Body for a bounded write-through tee before the forward runs
        // (the forward is inside next()), then log the captured prefix once it returns. Works on both
        // request branches, so retry/reuse routes capture the response too.
        var originalResponseBody = context.Response.Body;
        ResponsePrefixStream? responseTee = null;
        if (responseMax > 0)
        {
            responseTee = new ResponsePrefixStream(originalResponseBody, responseMax);
            context.Response.Body = responseTee;
        }

        try
        {
            if (requestMax > 0 && context.Request.Body.CanSeek)
            {
                await CaptureFromBufferedBodyAsync(context, requestMax, next);
            }
            else if (requestMax > 0)
            {
                context.Items[NextKey]  = next;
                context.Items[LimitKey] = requestMax;
                await _pipeline(context);
            }
            else
            {
                await next(context);
            }
        }
        finally
        {
            if (responseTee is not null)
            {
                LogResponse(context, responseTee);
                context.Response.Body = originalResponseBody;
                responseTee.ReturnBuffer();
            }
        }
    }

    // Reads up to maxSize off the already-buffered, seekable request body, rewinds, logs the prefix,
    // forwards. Position is left at 0 for the forward and any retry replay. No EnableBuffering — the
    // gateway's buffer is reused in place, staying under its budget.
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
                _logger.LogInformation(
                    "Captured request body for path {Path} route {RouteId}: {Body}",
                    context.Request.Path.Value ?? "", RouteId(context), body);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await next(context);
    }

    private void LogResponse(HttpContext context, ResponsePrefixStream tee)
    {
        if (!_logger.IsEnabled(LogLevel.Information)) return;

        var body = Encoding.UTF8.GetString(tee.Prefix);
        if (tee.Truncated) body += "... (truncated)";
        _logger.LogInformation(
            "Captured response body for path {Path} route {RouteId}: {Body}",
            context.Request.Path.Value ?? "", RouteId(context), body);
    }

    private static string RouteId(HttpContext context) =>
        context.Items.TryGetValue("ConduitSharp.RouteId", out var r) && r is string id ? id : "";

    private int RequestMaxSize(JsonElement config)  => DirectionMaxSize(config, "request");
    private int ResponseMaxSize(JsonElement config) => DirectionMaxSize(config, "response");

    // The direction's effective maxSize, or 0 when it is not captured. Clamped to the ceiling as well
    // as validated, so the bound holds even if a host ever skips ValidateConfig. Block absent or
    // maxSize <= 0 means "do not capture this direction"; block present without maxSize means the default.
    private int DirectionMaxSize(JsonElement config, string direction)
    {
        if (config.ValueKind != JsonValueKind.Object
            || !config.TryGetProperty(direction, out var block)
            || block.ValueKind != JsonValueKind.Object)
            return 0;

        if (block.TryGetProperty("maxSize", out var maxSizeProp) && maxSizeProp.TryGetInt32(out var parsed))
            return parsed <= 0 ? 0 : Math.Min(parsed, _maxAllowedSize);

        return DefaultMaxSize;
    }

    /// <summary>
    /// Applies the route's request <c>maxSize</c> to what HttpLogging captures, and stamps the matched
    /// route id + path into the same combined record so it is attributable in Loki.
    /// </summary>
    private sealed class PerRouteBodyLimitInterceptor : IHttpLoggingInterceptor
    {
        public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logging)
        {
            if (logging.HttpContext.Items.TryGetValue(LimitKey, out var value) && value is int limit)
                logging.RequestBodyLogLimit = limit;

            if (logging.HttpContext.Items.TryGetValue("ConduitSharp.RouteId", out var routeId) && routeId is string id)
                logging.AddParameter("conduitsharp.route_id", id);
            logging.AddParameter("conduitsharp.path", logging.HttpContext.Request.Path.Value ?? "");

            return default;
        }

        // Request bodies only — the response is captured by ResponsePrefixStream, not HttpLogging.
        public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logging) => default;
    }

    /// <summary>
    /// Bounded write-through tee over the response body: copies the first <c>maxSize</c> bytes into a
    /// pooled buffer as they are written, passes everything through untouched, and flags truncation.
    /// Swapping <see cref="HttpResponse.Body"/> also reroutes <c>BodyWriter</c> through it, so YARP's
    /// forward (which writes the response) is captured.
    /// </summary>
    private sealed class ResponsePrefixStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _max;
        private readonly byte[] _buffer;
        private int _captured;

        public ResponsePrefixStream(Stream inner, int maxSize)
        {
            _inner  = inner;
            _max    = maxSize;
            _buffer = ArrayPool<byte>.Shared.Rent(maxSize);
        }

        public bool Truncated { get; private set; }
        public ReadOnlySpan<byte> Prefix => _buffer.AsSpan(0, _captured);
        public void ReturnBuffer() => ArrayPool<byte>.Shared.Return(_buffer);

        private void Capture(ReadOnlySpan<byte> data)
        {
            var take = Math.Min(_max - _captured, data.Length);
            if (take > 0)
            {
                data[..take].CopyTo(_buffer.AsSpan(_captured));
                _captured += take;
            }
            if (data.Length > take) Truncated = true;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            _inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Re-homes HttpLogging's loggers under this plugin's category so request capture obeys this
    /// plugin's log level rather than the framework's Warning-filtered <c>Microsoft.AspNetCore.*</c>,
    /// and lands in Loki tagged as body-capture.
    /// </summary>
    private sealed class BodyCaptureLoggerFactory(ILoggerFactory inner) : ILoggerFactory
    {
        private static readonly string Category = typeof(StreamingBodyCapturePlugin).FullName!;

        public ILogger CreateLogger(string categoryName) =>
            inner.CreateLogger(categoryName.StartsWith("Microsoft.AspNetCore.HttpLogging", StringComparison.Ordinal)
                ? Category
                : categoryName);

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
        public void Dispose() { }
    }
}

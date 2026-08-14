using System.Buffers;
using System.Text;
using System.Text.Json;
using ConduitSharp.Core.Logging;
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
/// Logs a bounded prefix of the request body, the response body, or both, chosen per direction:
///
/// <code>
/// { "request": { "maxSize": 4096 }, "response": { "maxSize": 8192 } }
/// </code>
///
/// A direction is captured only when its block is present with a positive <c>maxSize</c>.
/// <see cref="IPipelinePlugin.ReadsRequestBody"/> stays <c>false</c>, so capture never forces the
/// gateway to buffer. Both prefixes are RAM-only and declared through
/// <see cref="CaptureMemoryBytes"/>, so the gateway reserves them against
/// <c>Gateway:RequestLimits:MaxRamBufferedBodyBytes</c> and sheds with a 503 under a flood.
/// </summary>
public sealed class BodyCapturePlugin : IPipelinePlugin
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

    private  const string NextKey  = "conduitsharp.body-capture.next";

    private readonly RequestDelegate _pipeline;
    private readonly ILogger _logger;

    public BodyCapturePlugin(ILoggerFactory loggerFactory, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger(typeof(BodyCapturePlugin).FullName!);

        var requestedCeiling = configuration?.GetValue<long?>("BodyCapture:MaxCaptureBytes") ?? DefaultMaxAllowedSize;
        if (requestedCeiling > int.MaxValue)
            throw new InvalidOperationException(
                $"Plugin 'body-capture' config error: 'BodyCapture:MaxCaptureBytes' of {Mb(requestedCeiling)} exceeds the " +
                $"{Mb(int.MaxValue)} maximum a captured prefix can address. Send full bodies to a file sink " +
                "(body-capture-file) instead of holding them in memory.");

        _maxAllowedSize = (int)Math.Max(requestedCeiling, DefaultMaxSize);

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new BodyCaptureLoggerFactory(loggerFactory, typeof(BodyCapturePlugin).FullName!));
        services.AddLogging();
        services.AddHttpLogging(options =>
        {
            options.LoggingFields = HttpLoggingFields.RequestBody;
            options.CombineLogs   = true;
        });
        services.AddHttpLoggingInterceptor<PerRouteBodyLimitInterceptor>();

        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UseHttpLogging();
        app.Run(context => ((RequestDelegate)context.Items[NextKey]!)(context));
        _pipeline = app.Build();
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:F1} MiB";

    public PluginName Name => PluginName.Custom;
    public string? Variant => "body-capture";
    public string Id => "body-capture";

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

        var originalResponseBody = context.Response.Body;
        BoundedTeeStream? responseTee = null;
        if (responseMax > 0)
        {
            responseTee = new BoundedTeeStream(originalResponseBody, responseMax);
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
                context.Items[PerRouteBodyLimitInterceptor.LimitKey] = requestMax;
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
                    context.Request.Path.Value.ForLog(), RouteId(context), body.ForLog());
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await next(context);
    }

    private void LogResponse(HttpContext context, BoundedTeeStream tee)
    {
        if (!_logger.IsEnabled(LogLevel.Information)) return;

        var body = Encoding.UTF8.GetString(tee.Captured.Span);
        if (tee.Truncated) body += "... (truncated)";
        _logger.LogInformation(
            "Captured response body for path {Path} route {RouteId}: {Body}",
            context.Request.Path.Value.ForLog(), RouteId(context), body.ForLog());
    }

    private static string RouteId(HttpContext context) =>
        context.Items.TryGetValue("ConduitSharp.RouteId", out var r) && r is string id ? id : "";

    private int RequestMaxSize(JsonElement config)  => DirectionMaxSize(config, "request");
    private int ResponseMaxSize(JsonElement config) => DirectionMaxSize(config, "response");

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
}

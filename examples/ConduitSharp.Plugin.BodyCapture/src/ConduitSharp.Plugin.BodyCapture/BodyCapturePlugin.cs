using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ConduitSharp.Core.Routing;
using ConduitSharp.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace ConduitSharp.Plugin.BodyCapture;

public sealed class BodyCapturePlugin(ILogger<BodyCapturePlugin> logger) : IPipelinePlugin
{
    /// <summary>
    /// Applied when a route omits <c>maxSize</c>. Absent used to mean "read it all", which put a
    /// second, unbounded copy of the body in this plugin — outside the gateway's buffering budget,
    /// and on the large object heap for anything past 85 KiB. A bounded default keeps the capture
    /// proportional to the log line it becomes; a route that needs more says so.
    /// </summary>
    private const int DefaultMaxSize = 4 * 1024;

    public PluginName Name => PluginName.Custom;
    public string? Variant => "body-capture";
    public string Id => "body-capture";

    // Reads the request body — the gateway's startup validation uses this to reject pairing this
    // plugin with a streamOnly route, where the body would be a forward-only stream.
    public bool ReadsRequestBody => true;

    public void ValidateConfig(JsonElement config)
    {
        if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("maxSize", out var maxSizeProp))
        {
            if (maxSizeProp.ValueKind != JsonValueKind.Number || !maxSizeProp.TryGetInt32(out var maxSize) || maxSize <= 0)
            {
                throw new InvalidOperationException("Plugin 'body-capture' config error: 'maxSize' must be a positive integer.");
            }
        }
    }

    public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
    {
        try
        {
            var maxSize = DefaultMaxSize;
            if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("maxSize", out var maxSizeProp)
                && maxSizeProp.TryGetInt32(out var parsedSize) && parsedSize > 0)
            {
                maxSize = parsedSize;
            }

            // The gateway already buffered the body into a budget-capped seekable stream (this plugin
            // is barred from streamOnly routes at startup), so we read it directly and rewind — no
            // EnableBuffering(), which would buffer a second copy *outside* the gateway's budget.
            context.Request.Body.Position = 0;

            string body;
            // Rent a pooled byte buffer and read bytes directly instead of a per-request
            // StreamReader + char[]: no 8 KB char[] alloc and no reader allocation on the hot
            // path. ReadAtLeastAsync fills up to maxSize (matching StreamReader.ReadBlockAsync's
            // loop-until-full) unless the body ends first.
            var buffer = ArrayPool<byte>.Shared.Rent(maxSize);
            try
            {
                var read = await context.Request.Body.ReadAtLeastAsync(
                    buffer.AsMemory(0, maxSize), maxSize, throwOnEndOfStream: false);
                body = Encoding.UTF8.GetString(buffer, 0, read);

                if (read == maxSize)
                {
                    var extraBuffer = new byte[1];
                    var extraRead = await context.Request.Body.ReadAsync(extraBuffer.AsMemory(0, 1));
                    if (extraRead > 0)
                    {
                        body += "... (truncated)";
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            context.Request.Body.Position = 0;

            var logLevel = LogLevel.Information;
            if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("logLevel", out var logLevelProp) && Enum.TryParse<LogLevel>(logLevelProp.GetString(), true, out var parsedLevel))
            {
                logLevel = parsedLevel;
            }

            if (logger.IsEnabled(logLevel))
            {
                var path = context.Request.Path.Value ?? string.Empty;
                logger.Log(logLevel, "Captured request body for path {Path}: {Body}", path, body);
            }

            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception in BodyCapturePlugin.ExecuteAsync");
            throw;
        }
    }
}

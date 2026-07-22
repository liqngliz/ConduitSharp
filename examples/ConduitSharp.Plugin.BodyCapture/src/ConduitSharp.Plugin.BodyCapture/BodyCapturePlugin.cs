using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ConduitSharp.Core.Routing;
using ConduitSharp.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace ConduitSharp.Plugin.BodyCapture;

public sealed class BodyCapturePlugin(ILogger<BodyCapturePlugin> logger) : IPipelinePlugin
{
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
            int? maxSize = null;
            if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("maxSize", out var maxSizeProp) && maxSizeProp.TryGetInt32(out var parsedSize))
            {
                maxSize = parsedSize;
            }

            // The gateway already buffered the body into a budget-capped seekable stream (this plugin
            // is barred from streamOnly routes at startup), so we read it directly and rewind — no
            // EnableBuffering(), which would buffer a second copy *outside* the gateway's budget.
            context.Request.Body.Position = 0;

            string body;
            if (maxSize.HasValue)
            {
                var buffer = new char[maxSize.Value];
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var read = await reader.ReadBlockAsync(buffer, 0, maxSize.Value);
                body = new string(buffer, 0, read);
                
                if (read == maxSize.Value)
                {
                    var extraBuffer = new char[1];
                    var extraRead = await reader.ReadAsync(extraBuffer, 0, 1);
                    if (extraRead > 0)
                    {
                        body += "... (truncated)";
                    }
                }
            }
            else
            {
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                body = await reader.ReadToEndAsync(context.RequestAborted);
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
                switch (logLevel)
                {
                    case LogLevel.Trace:
                        logger.LogTrace("Captured request body for path {Path}: {Body}", path, body);
                        break;
                    case LogLevel.Debug:
                        logger.LogDebug("Captured request body for path {Path}: {Body}", path, body);
                        break;
                    case LogLevel.Information:
                        logger.LogInformation("Captured request body for path {Path}: {Body}", path, body);
                        break;
                    case LogLevel.Warning:
                        logger.LogWarning("Captured request body for path {Path}: {Body}", path, body);
                        break;
                    case LogLevel.Error:
                        logger.LogError("Captured request body for path {Path}: {Body}", path, body);
                        break;
                    case LogLevel.Critical:
                        logger.LogCritical("Captured request body for path {Path}: {Body}", path, body);
                        break;
                }
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

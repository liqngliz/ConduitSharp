using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using ConduitSharp.Core.Routing;
using ConduitSharp.Core.Pipeline;
using Microsoft.Extensions.Configuration;
using System.Buffers;
using System.Text;

namespace ConduitSharp.Plugin.BodyCaptureToFile;

public sealed class BodyCaptureToFilePlugin : IPipelinePlugin, IDisposable
{
    public PluginName Name => PluginName.Custom;
    public string? Variant => "body-capture-file";
    public string Id => "body-capture-file";
    public bool ReadsRequestBody => true;

    private readonly Channel<(string time, string path, string traceId, byte[] buffer, int length, bool truncated)> _channel;
    private string _logPath = "/tmp/conduit-logs.json";
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writeTask;

    public BodyCaptureToFilePlugin(IConfiguration configuration)
    {
        int capacity = configuration.GetValue<int>("OTEL_BLRP_MAX_QUEUE_SIZE", 2048);
        _channel = Channel.CreateBounded<(string time, string path, string traceId, byte[] buffer, int length, bool truncated)>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest }
        );
        _writeTask = Task.Run(ProcessQueueAsync);
    }

    public void ValidateConfig(JsonElement config)
    {
        if (config.ValueKind == JsonValueKind.Object)
        {
            if (config.TryGetProperty("logPath", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
            {
                var path = pathProp.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _logPath = path;
                }
            }
            if (config.TryGetProperty("maxSize", out var maxSizeProp))
            {
                if (maxSizeProp.ValueKind != JsonValueKind.Number || !maxSizeProp.TryGetInt32(out var maxSize) || maxSize <= 0)
                {
                    throw new InvalidOperationException("Plugin 'body-capture-file' config error: 'maxSize' must be a positive integer.");
                }
            }
        }
    }

    public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
    {
        int? maxSize = null;
        if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("maxSize", out var maxSizeProp) && maxSizeProp.TryGetInt32(out var parsedSize))
        {
            maxSize = parsedSize;
        }

        context.Request.Body.Position = 0;

        byte[] buffer;
        int length;
        bool truncated = false;

        if (maxSize.HasValue)
        {
            buffer = ArrayPool<byte>.Shared.Rent(maxSize.Value);
            length = await context.Request.Body.ReadAsync(buffer, 0, maxSize.Value);
            
            if (length == maxSize.Value)
            {
                var extraBuffer = ArrayPool<byte>.Shared.Rent(1);
                var extraRead = await context.Request.Body.ReadAsync(extraBuffer, 0, 1);
                ArrayPool<byte>.Shared.Return(extraBuffer);
                if (extraRead > 0)
                {
                    truncated = true;
                }
            }
        }
        else
        {
            using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms);
            buffer = ArrayPool<byte>.Shared.Rent((int)ms.Length);
            Array.Copy(ms.GetBuffer(), buffer, ms.Length);
            length = (int)ms.Length;
        }

        context.Request.Body.Position = 0;

        var time = DateTime.UtcNow.ToString("O");
        var path = context.Request.Path.Value ?? string.Empty;
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        if (!_channel.Writer.TryWrite((time, path, traceId, buffer, length, truncated)))
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await next(context);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            var bufferWriter = new ArrayBufferWriter<byte>(65536);
            
            while (await _channel.Reader.WaitToReadAsync(_cts.Token))
            {
                using var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 65536, useAsync: true);
                
                while (_channel.Reader.TryRead(out var entry))
                {
                    bufferWriter.Clear();
                    
                    using (var writer = new Utf8JsonWriter(bufferWriter))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("time", entry.time);
                        writer.WriteString("path", entry.path);
                        writer.WriteString("traceId", entry.traceId);
                        
                        var suffix = "... (truncated)".AsSpan();
                        var charBuffer = ArrayPool<char>.Shared.Rent(entry.length + suffix.Length);
                        var charCount = Encoding.UTF8.GetChars(entry.buffer, 0, entry.length, charBuffer, 0);
                        
                        if (entry.truncated)
                        {
                            suffix.CopyTo(charBuffer.AsSpan(charCount));
                            charCount += suffix.Length;
                        }
                        
                        writer.WriteString("body", charBuffer.AsSpan(0, charCount));
                        ArrayPool<char>.Shared.Return(charBuffer);
                        
                        writer.WriteEndObject();
                    }
                    
                    bufferWriter.Write(new[] { (byte)'\n' });
                    await stream.WriteAsync(bufferWriter.WrittenMemory);
                    
                    ArrayPool<byte>.Shared.Return(entry.buffer);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        
        while (_channel.Reader.TryRead(out var entry))
        {
            ArrayPool<byte>.Shared.Return(entry.buffer);
        }
        
        _cts.Dispose();
    }
}

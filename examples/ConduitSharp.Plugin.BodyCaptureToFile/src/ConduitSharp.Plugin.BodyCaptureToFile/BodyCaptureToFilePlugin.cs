using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using ConduitSharp.Core.Routing;
using ConduitSharp.Core.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    // ponytail: single sink path for the plugin instance. The plugin is a shared singleton, so if two
    // routes set different logPath the last ValidateConfig wins for all. One file per gateway is the
    // intended layout; per-route files would need the path carried per entry through the channel.
    private string _logPath = "/tmp/conduit-logs.json";
    // Rolled at this size, keeping one .1 backup, so the sink is bounded at ~2x. Appending forever
    // fills a real disk (ENOSPC kills the writer) or hits the size cap on a tmpfs mount.
    private long _maxFileBytes = 128L * 1024 * 1024;
    private readonly ILogger<BodyCaptureToFilePlugin>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writeTask;

    // logger is optional so the plugin still constructs where nothing registers one (the tests do
    // exactly that); the gateway's DI supplies it in a real host.
    public BodyCaptureToFilePlugin(IConfiguration configuration, ILogger<BodyCaptureToFilePlugin>? logger = null)
    {
        _logger = logger;
        int capacity = configuration.GetValue<int>("OTEL_BLRP_MAX_QUEUE_SIZE", 2048);
        // DropWrite, not DropOldest: DropOldest silently evicts the queued entry whose byte[] is
        // rented from ArrayPool, and there is no drop callback to return it — that leaks pooled
        // buffers under exactly the backpressure this plugin is benchmarked at. DropWrite makes the
        // full-channel case fall to TryWrite==false below, where the buffer IS returned.
        _channel = Channel.CreateBounded<(string time, string path, string traceId, byte[] buffer, int length, bool truncated)>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropWrite }
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
            if (config.TryGetProperty("maxFileBytes", out var maxFileProp))
            {
                if (maxFileProp.ValueKind != JsonValueKind.Number || !maxFileProp.TryGetInt64(out var maxFileBytes) || maxFileBytes <= 0)
                {
                    throw new InvalidOperationException("Plugin 'body-capture-file' config error: 'maxFileBytes' must be a positive integer.");
                }
                _maxFileBytes = maxFileBytes;
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
                bool rollNow;
                using (var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 65536, useAsync: true))
                {
                while (_channel.Reader.TryRead(out var entry))
                {
                    try
                    {
                        bufferWriter.Clear();

                        using (var writer = new Utf8JsonWriter(bufferWriter))
                        {
                            writer.WriteStartObject();
                            writer.WriteString("time", entry.time);
                            writer.WriteString("path", entry.path);
                            writer.WriteString("traceId", entry.traceId);

                            // ponytail: a maxSize cut can split a multibyte UTF-8 char at the byte
                            // boundary; GetChars emits U+FFFD for the partial tail. Fine for a debug
                            // body dump — upgrade to a Decoder if exact truncation ever matters.
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
                    }
                    finally
                    {
                        // Return the pooled buffer even if the write throws, so an IO error can't
                        // leak it out of the pool.
                        ArrayPool<byte>.Shared.Return(entry.buffer);
                    }
                }

                    rollNow = stream.Length >= _maxFileBytes;
                }

                if (rollNow)
                {
                    Roll();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // This task is the only writer: once it dies, capture stops for the life of the process
            // while every request still succeeds and the plugin still looks healthy. Silence here is
            // the same failure shape as an over-sized OTLP batch — say so loudly instead.
            _logger?.LogError(ex,
                "BodyCaptureToFile writer stopped; request bodies are NO LONGER being captured to {LogPath}", _logPath);
        }
    }

    // Single backup, overwritten: bounds the sink at ~2x _maxFileBytes with no scheduler and no
    // dependency. A log shipper (promtail, the collector's filelog receiver) follows the rename.
    private void Roll()
    {
        try
        {
            var backup = _logPath + ".1";
            File.Delete(backup);
            File.Move(_logPath, backup);
        }
        catch (Exception ex)
        {
            // A failed roll is not fatal — the next iteration reopens and keeps appending.
            _logger?.LogError(ex, "BodyCaptureToFile could not roll {LogPath}; it will keep growing", _logPath);
        }
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

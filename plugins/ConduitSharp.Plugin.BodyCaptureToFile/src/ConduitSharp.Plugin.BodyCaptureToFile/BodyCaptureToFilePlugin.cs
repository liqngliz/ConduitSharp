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

/// <summary>
/// Captures a bounded prefix of the request body, the response body, or both, to a rolling JSONL sink
/// via a bounded channel and a background writer. Config is nested by direction:
/// <code>{ "request": { "maxSize": 4096 }, "response": { "maxSize": 8192 }, "logPath": "...", "maxFileBytes": ... }</code>
/// A direction is captured only when its block is present with a positive <c>maxSize</c>. The request
/// is read off the gateway's buffered body; the response is copied by a bounded write-through tee.
/// </summary>
public sealed class BodyCaptureToFilePlugin : IPipelinePlugin, IDisposable
{
    /// <summary>
    /// Applied when a direction's block is present but omits <c>maxSize</c>. Absent used to mean
    /// "read it all" — a MemoryStream copy plus a pooled copy of the whole body, both outside the
    /// gateway's buffering budget and both on the large object heap past 85 KiB. A bounded default
    /// keeps the capture proportional to the log entry it becomes; a route that needs more says so.
    /// </summary>
    private const int DefaultMaxSize = 4 * 1024;

    public PluginName Name => PluginName.Custom;
    public string? Variant => "body-capture-file";
    public string Id => "body-capture-file";

    // Static, so it cannot vary by config: the plugin always declares it needs the buffered request
    // body. Request capture reads that buffer directly. A response-only route therefore still buffers
    // the request even though it captures nothing from it — the cost of a per-plugin (not per-config)
    // property. Response capture itself needs no buffering; it tees Response.Body.
    public bool ReadsRequestBody => true;

    private readonly Channel<(string time, string path, string traceId, byte[] buffer, int length, bool truncated, string direction)> _channel;
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
        _channel = Channel.CreateBounded<(string time, string path, string traceId, byte[] buffer, int length, bool truncated, string direction)>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropWrite }
        );
        _writeTask = Task.Run(ProcessQueueAsync);

        // The queue can hold this much pooled RAM beyond what any in-flight request has reserved
        // (see CaptureMemoryBytes). Stated once at startup so it is a known number rather than one
        // discovered from a memory graph.
        _logger?.LogInformation(
            "BodyCaptureToFile queue holds up to {Capacity} entries; retained capture RAM is bounded by capacity x the route's maxSize",
            capacity);
    }

    /// <summary>
    /// The pooled <c>maxSize</c> buffer this plugin rents per request, declared so the gateway
    /// reserves it against <c>Gateway:RequestLimits:MaxRamBufferedBodyBytes</c> and sheds with a 503
    /// instead of letting it multiply by concurrency unbudgeted.
    ///
    /// Note what this does <em>not</em> cover. The buffer is handed to a bounded channel and returned
    /// to the pool only when the background writer drains it, which can be after the request that
    /// rented it has completed and released its reservation. So the queue can retain up to
    /// <c>OTEL_BLRP_MAX_QUEUE_SIZE</c> × <c>maxSize</c> beyond what is reserved at any instant — a
    /// hard ceiling (the channel is <c>DropWrite</c>), but one the request-scoped budget cannot see.
    /// It is logged at startup so the number is visible rather than inferred.
    ///
    /// The sink's <em>disk</em> use is deliberately not budgeted here: <c>MaxDiskBufferedBodyBytes</c>
    /// meters transient per-request spill that is released when the request ends, whereas this writes
    /// a persistent rolling log already bounded at ~2× <c>maxFileBytes</c> by <see cref="Roll"/>.
    /// Reserving those bytes per request would ratchet that budget to zero and 503 the gateway
    /// permanently, because they are never released.
    /// </summary>
    public int CaptureMemoryBytes(JsonElement config) => DirectionMaxSize(config, "request") + DirectionMaxSize(config, "response");

    // The direction's effective maxSize, or 0 when that direction is not captured. Block absent or
    // maxSize <= 0 means off; block present without maxSize means the default.
    private static int DirectionMaxSize(JsonElement config, string direction)
    {
        if (config.ValueKind != JsonValueKind.Object
            || !config.TryGetProperty(direction, out var block)
            || block.ValueKind != JsonValueKind.Object)
            return 0;
        if (block.TryGetProperty("maxSize", out var m) && m.TryGetInt32(out var parsed))
            return parsed <= 0 ? 0 : parsed;
        return DefaultMaxSize;
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
            if (config.TryGetProperty("maxSize", out _))
            {
                throw new InvalidOperationException(
                    "Plugin 'body-capture-file' config uses the removed flat shape ({ \"maxSize\": N }). Nest it by "
                    + "direction: { \"request\": { \"maxSize\": N } } and/or { \"response\": { \"maxSize\": M } }.");
            }
            foreach (var direction in new[] { "request", "response" })
            {
                if (config.TryGetProperty(direction, out var block) && block.ValueKind == JsonValueKind.Object
                    && block.TryGetProperty("maxSize", out var m)
                    && (m.ValueKind != JsonValueKind.Number || !m.TryGetInt32(out _)))
                {
                    throw new InvalidOperationException(
                        $"Plugin 'body-capture-file' config error: '{direction}.maxSize' must be an integer.");
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
        var requestMax  = DirectionMaxSize(config, "request");
        var responseMax = DirectionMaxSize(config, "response");

        if (requestMax > 0)
            await CaptureRequestAsync(context, requestMax);

        // Response capture: bounded write-through tee, enqueued after the forward writes the body.
        var originalResponseBody = context.Response.Body;
        ResponsePrefixStream? responseTee = null;
        if (responseMax > 0)
        {
            responseTee = new ResponsePrefixStream(originalResponseBody, responseMax);
            context.Response.Body = responseTee;
        }

        try
        {
            await next(context);
        }
        finally
        {
            if (responseTee is not null)
            {
                Enqueue(context, "response", responseTee.Buffer, responseTee.Captured, responseTee.Truncated);
                context.Response.Body = originalResponseBody;
            }
        }
    }

    // Reads the request prefix off the gateway's buffered (seekable) body — this plugin declares
    // ReadsRequestBody, so the body is buffered by the time it runs.
    private async Task CaptureRequestAsync(HttpContext context, int maxSize)
    {
        context.Request.Body.Position = 0;

        var buffer = ArrayPool<byte>.Shared.Rent(maxSize);
        // ReadAtLeastAsync, not ReadAsync: a single read may return short on a stream that still has
        // data, which would both cut the capture and leave `truncated` false.
        var length = await context.Request.Body.ReadAtLeastAsync(
            buffer.AsMemory(0, maxSize), maxSize, throwOnEndOfStream: false);

        var truncated = false;
        if (length == maxSize)
        {
            var extra = ArrayPool<byte>.Shared.Rent(1);
            truncated = await context.Request.Body.ReadAsync(extra, 0, 1) > 0;
            ArrayPool<byte>.Shared.Return(extra);
        }

        context.Request.Body.Position = 0;

        Enqueue(context, "request", buffer, length, truncated);
    }

    // Hands a pooled buffer to the writer channel. The writer owns and returns it; on a full channel
    // (DropWrite) TryWrite fails and we return it here so it never leaks.
    private void Enqueue(HttpContext context, string direction, byte[] buffer, int length, bool truncated)
    {
        var time    = DateTime.UtcNow.ToString("O");
        var path    = context.Request.Path.Value ?? string.Empty;
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        if (!_channel.Writer.TryWrite((time, path, traceId, buffer, length, truncated, direction)))
            ArrayPool<byte>.Shared.Return(buffer);
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
                            writer.WriteString("direction", entry.direction);

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

    /// <summary>
    /// Bounded write-through tee over the response body: copies the first <c>maxSize</c> bytes into a
    /// pooled buffer as the response is written, passes everything through untouched, and flags
    /// truncation. Its <see cref="Buffer"/> is handed to the writer channel, which owns and returns it.
    /// </summary>
    private sealed class ResponsePrefixStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _max;

        public byte[] Buffer { get; }
        public int Captured { get; private set; }
        public bool Truncated { get; private set; }

        public ResponsePrefixStream(Stream inner, int maxSize)
        {
            _inner  = inner;
            _max    = maxSize;
            Buffer  = ArrayPool<byte>.Shared.Rent(maxSize);
        }

        private void Capture(ReadOnlySpan<byte> data)
        {
            var take = Math.Min(_max - Captured, data.Length);
            if (take > 0)
            {
                data[..take].CopyTo(Buffer.AsSpan(Captured));
                Captured += take;
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
}

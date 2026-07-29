using System.Diagnostics;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConduitSharp.E2E.Shared;

// Real-Kestrel harness for measuring the memory cost of request-body capture. There is one capture
// plugin now — StreamingBodyCapturePlugin (variant "body-capture") — which picks its path per request:
//   - route already buffered (retry / readsBody): reuse the seekable buffer, read the prefix off it
//   - plain streaming route: tee a <=ceiling prefix via HttpLogging
// So a route's shape (retry on/off), not a plugin choice, decides the behaviour.
//
// Both the gateway and the upstream run as real Kestrel servers on loopback in THIS process. The
// client streams the body (StreamingContent never materializes it) and the upstream drains+discards
// it, so neither side accumulates the body — any memory the sampler sees is the gateway's capture.
// Single-process, so RSS carries GC noise; PeakManagedHeap + PeakSpillBytes are the sharp signals.

public enum CaptureStyle { Capture, None }

/// <summary>Peak resource use observed across one request, sampled on a background loop.</summary>
public readonly record struct MemoryReading(
    long PeakRssBytes,
    long PeakManagedHeapBytes,
    long PeakSpillBytes,
    long AllocatedBytes,
    long ElapsedMs)
{
    public double PeakRssMB           => PeakRssBytes / 1024.0 / 1024.0;
    public double PeakManagedHeapMB   => PeakManagedHeapBytes / 1024.0 / 1024.0;
    public double PeakSpillMB         => PeakSpillBytes / 1024.0 / 1024.0;
    public double AllocatedMB         => AllocatedBytes / 1024.0 / 1024.0;

    public override string ToString() =>
        $"RSS {PeakRssMB,7:F1} MB | heap {PeakManagedHeapMB,7:F1} MB | spill {PeakSpillMB,7:F1} MB | " +
        $"alloc {AllocatedMB,8:F1} MB | {ElapsedMs,6} ms";
}

/// <summary>
/// Samples peak working set, peak managed heap, and peak bytes resident in a spill directory while a
/// body of work runs. Baseline RSS/heap are subtracted from an idle GC-collected floor so the reading
/// reflects the request, not the whole process footprint.
/// </summary>
public sealed class MemorySampler
{
    private readonly string _spillDir;
    private readonly CancellationTokenSource _cts = new();
    private readonly Process _proc = Process.GetCurrentProcess();
    private Task? _loop;
    private long _peakRss, _peakHeap, _peakSpill, _baselineRss, _baselineHeap, _startAllocated;
    private long _startTicks;

    public MemorySampler(string spillDir) => _spillDir = spillDir;

    public void Start()
    {
        // Collect so the floor excludes prior-test garbage, then measure the idle baseline to subtract.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        _proc.Refresh();
        _baselineRss    = _proc.WorkingSet64;
        _baselineHeap   = GC.GetGCMemoryInfo().HeapSizeBytes;
        _startAllocated = GC.GetTotalAllocatedBytes(precise: false);
        _peakRss = _peakHeap = _peakSpill = 0;
        _startTicks = Stopwatch.GetTimestamp();
        _loop = Task.Run(() => SampleLoopAsync(_cts.Token));
    }

    public MemoryReading Stop()
    {
        Sample(); // one final reading — the peak may be at the very end
        _cts.Cancel();
        try { _loop?.Wait(); } catch (AggregateException) { /* cancellation */ }
        var elapsedMs   = (long)Stopwatch.GetElapsedTime(_startTicks).TotalMilliseconds;
        var allocated   = GC.GetTotalAllocatedBytes(precise: false) - _startAllocated;
        return new MemoryReading(_peakRss, _peakHeap, _peakSpill, allocated, elapsedMs);
    }

    private async Task SampleLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Sample();
            try { await Task.Delay(20, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private void Sample()
    {
        _proc.Refresh();
        _peakRss  = Math.Max(_peakRss,  _proc.WorkingSet64 - _baselineRss);
        _peakHeap = Math.Max(_peakHeap, GC.GetGCMemoryInfo().HeapSizeBytes - _baselineHeap);

        long spill = 0;
        try
        {
            if (Directory.Exists(_spillDir))
                foreach (var f in Directory.EnumerateFiles(_spillDir))
                    try { spill += new FileInfo(f).Length; } catch { /* file rolled/deleted mid-scan */ }
        }
        catch { /* dir churned */ }
        _peakSpill = Math.Max(_peakSpill, spill);
    }
}

/// <summary>An HTTP body of <paramref name="TotalBytes"/> bytes generated on the fly — never held.</summary>
public sealed class StreamingContent : System.Net.Http.HttpContent
{
    public long TotalBytes { get; }
    private readonly byte _fill;

    public StreamingContent(long totalBytes, byte fill = (byte)'x', string mediaType = "application/octet-stream")
    {
        TotalBytes = totalBytes;
        _fill = fill;
        // Media type matters: HttpLogging (the tee) only captures bodies of text-ish media types
        // (text/*, application/json, ...). A binary type makes the tee a no-op — so a fair tee-vs-buffer
        // capture comparison must use a type HttpLogging will actually tee.
        Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        var chunk = new byte[64 * 1024];
        Array.Fill(chunk, _fill);
        long remaining = TotalBytes;
        while (remaining > 0)
        {
            var n = (int)Math.Min(chunk.Length, remaining);
            await stream.WriteAsync(chunk.AsMemory(0, n));
            remaining -= n;
        }
    }

    protected override bool TryComputeLength(out long length) { length = TotalBytes; return true; }
}

/// <summary>Real-Kestrel upstream that drains and discards request bodies. Optionally fails the first
/// N requests with 503 so the gateway's retry loop replays (and re-reads) the body.</summary>
public sealed class DiscardUpstream : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _calls;
    public string BaseUrl { get; }
    public int FailFirst { get; set; }
    public int RequestCount => _calls;

    private DiscardUpstream(WebApplication app, string baseUrl) { _app = app; BaseUrl = baseUrl; }

    public static async Task<DiscardUpstream> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = null);
        builder.Logging.ClearProviders();
        var app = builder.Build();

        DiscardUpstream? self = null;
        app.Run(async ctx =>
        {
            var n = Interlocked.Increment(ref self!._calls);
            var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);
            try { while (await ctx.Request.Body.ReadAsync(buf) > 0) { /* discard */ } }
            finally { System.Buffers.ArrayPool<byte>.Shared.Return(buf); }

            if (n <= self._failFirst) { ctx.Response.StatusCode = 503; return; }
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("ok");
        });
        await app.StartAsync();
        self = new DiscardUpstream(app, app.Urls.First());
        return self;
    }

    private int _failFirst => FailFirst;
    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

/// <summary>Boots the full ConduitSharp gateway as a real Kestrel server with exactly one capture
/// plugin (or none), a spill directory of its own, and body limits generous enough to admit the test
/// body.</summary>
public sealed class CaptureGatewayHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _routesPath;
    private readonly CaptureCountingLoggerProvider _captureLog;
    public string BaseUrl { get; }
    public string SpillDir { get; }

    /// <summary>How many capture log records were emitted — proves the capture path actually ran
    /// (HttpLogging skips buffering entirely when its log level is disabled).</summary>
    public int CaptureRecords => _captureLog.Count;

    private CaptureGatewayHost(WebApplication app, string baseUrl, string routesPath, string spillDir, CaptureCountingLoggerProvider captureLog)
    {
        _app = app; BaseUrl = baseUrl; _routesPath = routesPath; SpillDir = spillDir; _captureLog = captureLog;
    }

    public static async Task<CaptureGatewayHost> StartAsync(
        string routesJson,
        CaptureStyle style,
        long memoryBufferThresholdBytes = 1024 * 1024,
        long maxBodyBytes = 700L * 1024 * 1024,
        long maxTotalBufferedBytes = 700L * 1024 * 1024,
        int? streamingMaxCaptureBytes = null)
    {
        var spillDir = Directory.CreateTempSubdirectory("csharp-capture-spill-").FullName;
        var routesPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(routesPath, routesJson);

        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Test";
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = null);
        // MUST be Information-enabled: HttpLogging (the tee) skips buffering the body when its log
        // level is disabled, which would make the tee a no-op and every measurement a lie. This
        // provider is enabled but discards the formatted message (counting records) so the tee runs
        // for real without printing a 500 MB body.
        var captureLog = new CaptureCountingLoggerProvider();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddProvider(captureLog);

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gateway:RoutesPath"]                              = routesPath,
            ["Gateway:RequestLimits:MaxRequestBodyBytes"]       = maxBodyBytes.ToString(),
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"] = maxTotalBufferedBytes.ToString(),
            ["Gateway:RequestLimits:MaxRamBufferedBodyBytes"]= maxTotalBufferedBytes.ToString(),
            ["Gateway:RequestLimits:RamBufferThresholdBytes"]= memoryBufferThresholdBytes.ToString(),
            ["Gateway:RequestLimits:SpillDirectory"]            = spillDir,
            ["BodyCapture:MaxCaptureBytes"]                     = streamingMaxCaptureBytes?.ToString(),
        });

        if (style == CaptureStyle.Capture)
            builder.Services.AddSingleton<IPipelinePlugin, ConduitSharp.Plugin.BodyCapture.StreamingBodyCapturePlugin>();

        builder.AddConduitSharpGateway();
        var app = builder.Build();
        app.UseConduitSharpGateway();
        await app.StartAsync();

        return new CaptureGatewayHost(app, app.Urls.First(), routesPath, spillDir, captureLog);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        try { File.Delete(_routesPath); } catch { }
        try { Directory.Delete(SpillDir, recursive: true); } catch { }
    }

    // Enabled at Information (so HttpLogging actually buffers + emits), but the sink drops the message
    // after counting it — the tee's real memory cost is the buffered body, which happens before Log().
    // Counts ONLY records under the capture category: the tee's HttpLogging records are re-homed there
    // (BodyCaptureLoggerFactory) and the reuse branch logs there directly, so this excludes unrelated
    // gateway/YARP Information logs that would otherwise inflate the count.
    private sealed class CaptureCountingLoggerProvider : ILoggerProvider
    {
        private static readonly string CaptureCategory =
            typeof(ConduitSharp.Plugin.BodyCapture.StreamingBodyCapturePlugin).FullName!;
        private int _count;
        public int Count => _count;
        public ILogger CreateLogger(string categoryName) =>
            categoryName == CaptureCategory ? new CountingLogger(this) : EnabledNullLogger.Instance;
        public void Dispose() { }

        // Enabled (so HttpLogging still buffers), but discards.
        private sealed class EnabledNullLogger : ILogger
        {
            public static readonly EnabledNullLogger Instance = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
            public void Log<TState>(LogLevel l, EventId e, TState s, Exception? ex, Func<TState, Exception?, string> f) { }
        }

        private sealed class CountingLogger(CaptureCountingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Information) Interlocked.Increment(ref owner._count);
            }
        }
    }
}

/// <summary>Builds the routes.json for one capture case.</summary>
public static class CaptureRoutes
{
    public static string Build(string upstreamBaseUrl, CaptureStyle style, int maxSize, bool retry)
    {
        var pluginsArray = style == CaptureStyle.Capture
            ? $$"""[{ "name": "custom", "variant": "body-capture", "order": 1, "config": { "request": { "maxSize": {{maxSize}} } } }]"""
            : "[]";
        var retryBlock = retry ? """, "retry": { "maxAttempts": 2, "delayMs": 0 }""" : "";

        return $$"""
        {
          "routes": [{
            "id": "capture-route",
            "route": { "match": { "path": "/{**rest}" } },
            "cluster": {
              "loadBalancingPolicy": "RoundRobin",
              "destinations": { "node-0": { "address": "{{upstreamBaseUrl}}" } },
              "httpRequest": { "activityTimeout": "00:02:00" }
            }{{retryBlock}},
            "plugins": {{pluginsArray}}
          }]
        }
        """;
    }
}

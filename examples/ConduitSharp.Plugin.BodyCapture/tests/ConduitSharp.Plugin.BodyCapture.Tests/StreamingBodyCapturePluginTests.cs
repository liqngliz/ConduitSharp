using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ConduitSharp.Plugin.BodyCapture.Tests;

public sealed class StreamingBodyCapturePluginTests
{
    // The plugin delegates capture to the framework's HttpLogging middleware, which logs under its
    // own category rather than through an injected ILogger<T>. So these tests assert on what the
    // host's ILoggerFactory actually receives — the same records that reach OTLP → Loki in prod.
    private static (StreamingBodyCapturePlugin Plugin, CapturedLogs Logs) Build()
    {
        var logs = new CapturedLogs();
        var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace); // HttpLogging's category is Warning-filtered by default
            b.AddProvider(logs);
        });
        return (new StreamingBodyCapturePlugin(factory), logs);
    }

    // Models YARP's forward: the real request pipeline reads Request.Body to stream it upstream.
    // Draining to Stream.Null is exactly what the in-memory upstream does, and it drives the
    // HttpLogging capture stream the plugin installed.
    private static Task ForwardByDrainingBody(HttpContext ctx) => ctx.Request.Body.CopyToAsync(Stream.Null);

    private static DefaultHttpContext Request(string body, string? contentType = "application/json")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "POST";
        if (contentType is not null) context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context;
    }

    [Fact]
    public void ValidateConfig_ValidMaxSize_DoesNotThrow()
    {
        var config = JsonDocument.Parse("""{ "maxSize": 1024 }""").RootElement;
        Build().Plugin.ValidateConfig(config); // Should not throw
    }

    [Fact]
    public void ValidateConfig_InvalidMaxSize_Throws()
    {
        var config = JsonDocument.Parse("""{ "maxSize": -5 }""").RootElement;
        Assert.Throws<InvalidOperationException>(() => Build().Plugin.ValidateConfig(config));
    }

    [Fact]
    public void ValidateConfig_MaxSizeAtCeiling_DoesNotThrow()
    {
        var config = JsonDocument.Parse("""{ "maxSize": 32768 }""").RootElement;
        Build().Plugin.ValidateConfig(config); // 32 KiB is the ceiling, not past it
    }

    [Fact]
    public void ValidateConfig_MaxSizeAboveCeiling_Throws()
    {
        // Capture memory rides the streaming path, which never reserves against the gateway's
        // buffering budget — so an unbounded maxSize is heap nothing can shed. 32 KiB also keeps
        // the prefix under the 85 KiB LOH threshold.
        var config = JsonDocument.Parse("""{ "maxSize": 32769 }""").RootElement;
        var ex = Assert.Throws<InvalidOperationException>(() => Build().Plugin.ValidateConfig(config));
        Assert.Contains("32768", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ClampsCaptureToCeiling_EvenIfValidateConfigWasSkipped()
    {
        // Defence in depth: the ceiling bounds heap, so it must not depend on a host remembering
        // to call ValidateConfig. A 1 MB request never yields more than a 32 KiB prefix.
        var (plugin, logs) = Build();
        var context = Request(new string('z', 64 * 1024));

        var config = JsonDocument.Parse("""{ "maxSize": 1048576 }""").RootElement;
        await plugin.ExecuteAsync(context, config, ForwardByDrainingBody);

        Assert.Contains("[Truncated by RequestBodyLogLimit]", logs.Text);
    }

    [Fact]
    public void ReadsRequestBody_IsFalse_SoRouteKeepsStreaming()
    {
        // The whole point: this plugin must NOT force the gateway's buffering path.
        Assert.False(Build().Plugin.ReadsRequestBody);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesBody_AsItStreamsThrough()
    {
        var (plugin, logs) = Build();
        var context = Request("hello full body");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, ForwardByDrainingBody);

        Assert.Contains("hello full body", logs.Text);
    }

    [Fact]
    public async Task ExecuteAsync_TruncatesBody_WhenExceedsMaxSize()
    {
        var (plugin, logs) = Build();
        var context = Request("1234567890");

        var config = JsonDocument.Parse("""{ "maxSize": 5 }""").RootElement;
        await plugin.ExecuteAsync(context, config, ForwardByDrainingBody);

        Assert.Contains("RequestBody: 12345", logs.Text);
        Assert.Contains("[Truncated by RequestBodyLogLimit]", logs.Text);
        Assert.DoesNotContain("67890", logs.Text);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsBodyIntact_ToTheUpstream()
    {
        // The capture must be transparent: the downstream reader (the "upstream") sees every byte,
        // regardless of the small capture cap.
        var (plugin, _) = Build();

        var payload = Encoding.UTF8.GetBytes(new string('x', 200_000));
        var context = Request(new string('x', 200_000));

        var forwarded = new MemoryStream();
        var config = JsonDocument.Parse("""{ "maxSize": 64 }""").RootElement;
        await plugin.ExecuteAsync(context, config, ctx => ctx.Request.Body.CopyToAsync(forwarded));

        Assert.Equal(payload, forwarded.ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_RestoresOriginalBodyStream_AfterForward()
    {
        var (plugin, _) = Build();
        var context = Request("body");
        var original = context.Request.Body;

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, ForwardByDrainingBody);

        Assert.Same(original, context.Request.Body);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotLogBody_ForUnrecognizedMediaType()
    {
        // Behaviour inherited from HttpLogging's MediaTypeOptions: only text-ish bodies are logged.
        // A binary upload streams through untouched and unlogged rather than spraying bytes at Loki.
        var (plugin, logs) = Build();
        var context = Request("1234567890", contentType: "application/octet-stream");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, ForwardByDrainingBody);

        Assert.DoesNotContain("1234567890", logs.Text);
        Assert.Contains("Unrecognized Content-Type", logs.Text);
    }

    [Fact]
    public async Task ExecuteAsync_LogsUnderPluginCategory_NotHttpLoggings()
    {
        // Guards a silent-failure footgun: HttpLogging logs bodies at Information under
        // "Microsoft.AspNetCore.HttpLogging.*", and every stock appsettings.json in this repo
        // filters "Microsoft.AspNetCore" to Warning. If the category leaked through unrenamed,
        // production would drop every captured body while these tests still passed.
        var (plugin, logs) = Build();
        var context = Request("hello full body");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, ForwardByDrainingBody);

        Assert.Contains(typeof(StreamingBodyCapturePlugin).FullName!, logs.Categories);
        Assert.DoesNotContain(logs.Categories, c => c.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_BodiesSurvive_TheStockAspNetCoreWarningFilter()
    {
        // End-to-end proof of the above, through a real filter pipeline configured exactly like
        // the repo's appsettings.json ("Microsoft.AspNetCore": "Warning").
        var logs = new CapturedLogs();
        var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            b.AddProvider(logs);
        });
        var plugin = new StreamingBodyCapturePlugin(factory);

        await plugin.ExecuteAsync(Request("hello full body"), JsonDocument.Parse("{}").RootElement, ForwardByDrainingBody);

        Assert.Contains("hello full body", logs.Text);
    }

    [Fact]
    public async Task ExecuteAsync_LogRecordCarriesRouteIdAndPath_AlongsideBody()
    {
        // A captured body with no route attribution is unattributable in Loki. The interceptor
        // stamps the gateway's route id (context.Items) and the path into the SAME combined record.
        var (plugin, logs) = Build();
        var context = Request("attributed body");
        context.Items["ConduitSharp.RouteId"] = "route-orders";

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, ForwardByDrainingBody);

        var record = Assert.Single(logs.Records, r => r.Contains("attributed body"));
        Assert.Contains("route-orders", record);
        Assert.Contains("/api/test", record);
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task ExecuteAsync_ConcurrentRoutes_EachRecordPairsItsOwnRouteAndBody()
    {
        // One singleton plugin, many in-flight requests across "routes": every combined record
        // must pair route-i with body-i — a cross-pairing means per-request state leaked.
        var (plugin, logs) = Build();
        var config = JsonDocument.Parse("{}").RootElement;

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            var context = Request($"body-{i}-payload");
            context.Request.Path = $"/api/req-{i}";
            context.Items["ConduitSharp.RouteId"] = $"route-{i}";

            await plugin.ExecuteAsync(context, config, ForwardByDrainingBody);
        })));

        for (var i = 0; i < 50; i++)
        {
            var record = Assert.Single(logs.Records, r => r.Contains($"body-{i}-payload"));
            Assert.Contains($"route-{i}", record);
            Assert.Contains($"/api/req-{i}", record);
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly StringWriter _writer = new();
        private readonly List<string> _categories = [];
        private readonly List<string> _records = [];

        public string Text => _writer.ToString();
        public IReadOnlyList<string> Categories => _categories;
        public IReadOnlyList<string> Records
        {
            get { lock (_writer) return _records.ToList(); }
        }

        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);
        public void Dispose() => _writer.Dispose();

        private sealed class Recorder(CapturedLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
                Func<TState, Exception?, string> formatter)
            {
                var line = formatter(state, ex);
                lock (owner._writer)
                {
                    owner._categories.Add(category);
                    owner._records.Add(line);
                    owner._writer.WriteLine(line);
                }
            }
        }
    }
}

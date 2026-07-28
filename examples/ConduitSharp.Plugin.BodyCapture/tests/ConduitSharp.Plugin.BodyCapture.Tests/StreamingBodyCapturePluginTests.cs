using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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

    // How an upstream error actually surfaces: YARP's forward is terminal, reads the body to stream
    // it, and on an upstream failure writes a 502 — it does NOT throw (see ConsecutiveFailuresHealth
    // Policy, which detects failure by status code + IForwarderErrorFeature, not by catching).
    private static async Task ForwardDrainThenReturn502(HttpContext ctx)
    {
        await ctx.Request.Body.CopyToAsync(Stream.Null);
        ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
    }

    // Upstream unreachable — YARP fails to connect and writes 502 before the request body is read.
    private static Task Return502BeforeReading(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
        return Task.CompletedTask;
    }

    // A throw, by contrast, models what genuinely propagates through the plugin chain: a client
    // abort mid-request, or a plugin throwing in its post-next() code — NOT an upstream error.
    private static async Task ForwardDrainThenThrow(HttpContext ctx)
    {
        await ctx.Request.Body.CopyToAsync(Stream.Null);
        throw new IOException("client reset after body sent");
    }

    // A later auth/RBAC plugin rejecting the request: writes 401/403 and returns WITHOUT reading the
    // body or reaching the forward — the same short-circuit JwtAuthPlugin does. Only reachable when
    // capture is ordered BEFORE auth; in the shipped routes auth is order 1 and capture order 10, so
    // a rejection short-circuits before capture runs at all and neither branch sees the body.
    private static Task RejectWithoutReading(HttpContext ctx, int status)
    {
        ctx.Response.StatusCode = status;
        return Task.CompletedTask;
    }

    // Seekable body → the plugin takes its reuse branch (models a retry route or a body-reading
    // plugin having already buffered the body upstream in the chain).
    private static DefaultHttpContext SeekableRequest(string body, string? contentType = "application/json")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "POST";
        if (contentType is not null) context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body)); // CanSeek == true
        return context;
    }

    private static DefaultHttpContext Request(string body, string? contentType = "application/json")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "POST";
        if (contentType is not null) context.Request.ContentType = contentType;
        // Non-seekable: models the streaming path, so the plugin takes its HttpLogging tee branch
        // (the reuse branch fires only when the gateway has already buffered a seekable body — see
        // the retry/binary reuse tests in the integration suite).
        context.Request.Body = new NonSeekableStream(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        return context;
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
    public void ValidateConfig_CeilingIsConfigurable_RaisedCeilingAcceptsLargerMaxSize()
    {
        // BodyCapture:MaxCaptureBytes lifts the default 32 KiB ceiling. A 48 KiB maxSize that the
        // default rejects is accepted once the ceiling is raised to 64 KiB.
        var config = JsonDocument.Parse("""{ "maxSize": 49152 }""").RootElement;

        var factory = LoggerFactory.Create(_ => { });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BodyCapture:MaxCaptureBytes"] = "65536" })
            .Build();

        var raised = new StreamingBodyCapturePlugin(factory, configuration);
        raised.ValidateConfig(config); // 48 KiB is under the raised 64 KiB ceiling — no throw

        Assert.Throws<InvalidOperationException>(() => Build().Plugin.ValidateConfig(config)); // default still rejects
    }

    private static StreamingBodyCapturePlugin BuildWith(params (string Key, string Value)[] settings) =>
        new(LoggerFactory.Create(_ => { }),
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build());

    [Fact]
    public void Ctor_CeilingBeyondAddressableRange_ThrowsWithItsSize()
    {
        // A captured prefix is addressed by an int (HttpLogging's RequestBodyLogLimit), so an
        // oversized ceiling must say so plainly rather than die as an opaque conversion error.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BuildWith(("BodyCapture:MaxCaptureBytes", (4L * 1024 * 1024 * 1024).ToString()))); // 4 GiB
        Assert.Contains("4096.0 MiB", ex.Message);
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

    // -------------------------------------------------------------------------
    // Capture is scoped to its potential cause: the body is logged only when it entered the causal
    // chain — i.e. was actually forwarded (read). A body that never reached upstream (502 before
    // read, connection refused/dropped) or was rejected before the forward is not the cause of that
    // outcome, and logging it would be storing payloads for failures they did not cause. The tee's
    // read-gated capture IS that scoping, not a limitation of it.
    //
    // The forward is terminal (chain.Run) and turns upstream failure into a 502 RESPONSE, not an
    // exception, so the realistic cases below are 502-returning forwards; a throw models the
    // narrower client-abort / plugin-throw path.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TeePath_UpstreamReadsBodyThenReturns502_BodyIsCaptured_ItWasForwarded()
    {
        // The body was read and sent upstream, then upstream failed → 502. Here the body IS a
        // potential cause (a malformed payload upstream rejected), so it is in scope and logged.
        var (plugin, logs) = Build();
        var context = Request("tee: read then 502");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, ForwardDrainThenReturn502);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Contains("tee: read then 502", logs.Text);
    }

    [Fact]
    public async Task TeePath_UpstreamUnreachable_Returns502WithoutReadingBody_BodyNotCaptured_NotItsCause()
    {
        // Upstream unreachable: 502 written before the body is read. This is a transport/availability
        // failure — the body did not cause it and is not needed to diagnose it — so it is correctly
        // out of scope and not logged. Read-gated capture doing its job, not a gap.
        var (plugin, logs) = Build();
        var context = Request("tee: never read");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, Return502BeforeReading);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.DoesNotContain("tee: never read", logs.Text);
    }

    [Fact]
    public async Task ReusePath_UpstreamUnreachable_Returns502WithoutReadingBody_LogsAnyway_OverScopes()
    {
        // Counterpoint: the reuse branch logs the prefix BEFORE next(), unconditionally, because that
        // is where the already-buffered seekable body is in hand. So it records a body for a request
        // that never forwarded — a body that was not the cause. By the scope-to-cause rule this is a
        // mild over-capture, tolerated because it is the free path on routes that already buffer, and
        // because in the shipped ordering auth (order 1) has narrowed the traffic before capture runs.
        var (plugin, logs) = Build();
        var context = SeekableRequest("reuse: logged though never forwarded");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, Return502BeforeReading);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Contains("reuse: logged though never forwarded", logs.Text);
    }

    [Fact]
    public async Task TeePath_ReadsBodyThenThrows_BodyCaptured_ModelsClientAbortNotUpstreamError()
    {
        // A throw is NOT how upstream failure surfaces (that is a 502). It models a client abort
        // after the body was sent, or a plugin throwing post-next(). The body was read and forwarded,
        // so it was in the causal chain — HttpLogging still emits the record on the exception path.
        var (plugin, logs) = Build();
        var context = Request("tee: read then abort");

        await Assert.ThrowsAsync<IOException>(() =>
            plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, ForwardDrainThenThrow));

        Assert.Contains("tee: read then abort", logs.Text);
    }

    // -------------------------------------------------------------------------
    // Auth rejection (401 / 403). Same scope-to-cause rule: for a rejected request the AUTH decision
    // is the cause, not the payload, so the body is out of scope. In the shipped routes auth is order
    // 1 and capture order 10, so a rejection short-circuits before capture even runs — the body is
    // never logged regardless of branch. These tests cover the deliberate forensic arrangement
    // (capture ordered before auth to record what a rejected caller sent); only the reuse branch, by
    // logging pre-forward, actually widens scope to include it.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task TeePath_LaterAuthRejects_BodyNotCaptured(int status)
    {
        // Even ordered before auth, the tee needs the forward to read the body — a rejection
        // short-circuits before that, so nothing is teed. Read-gated capture keeps the rejected
        // payload out of scope; deliberately auditing rejected bodies is not possible on the
        // streaming path.
        var (plugin, logs) = Build();
        var context = Request($"tee: rejected {status}");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement,
            ctx => RejectWithoutReading(ctx, status));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.DoesNotContain($"tee: rejected {status}", logs.Text);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task ReusePath_OrderedBeforeAuth_RejectedBodyIsCaptured(int status)
    {
        // Widening scope on purpose: the reuse branch logs before next(), so ordering capture ahead
        // of auth on a buffered route DOES record what a rejected caller sent. This is a deliberate
        // forensic choice (attack-payload capture), not the default — it stores bodies that were not
        // the cause of the rejection, which is exactly what the scope-to-cause rule warns against, so
        // enable it only where that trade is intended. (Requires a buffered/seekable route; a plain
        // streaming route falls to the tee above and captures nothing.)
        var (plugin, logs) = Build();
        var context = SeekableRequest($"reuse: rejected {status}");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement,
            ctx => RejectWithoutReading(ctx, status));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Contains($"reuse: rejected {status}", logs.Text);
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

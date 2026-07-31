using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ConduitSharp.Plugin.BodyCapture.Tests;

public sealed class BodyCapturePluginTests
{
    private static (BodyCapturePlugin Plugin, CapturedLogs Logs) Build()
    {
        var logs = new CapturedLogs();
        var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(logs);
        });
        return (new BodyCapturePlugin(factory), logs);
    }

    private static Task ForwardByDrainingBody(HttpContext ctx) => ctx.Request.Body.CopyToAsync(Stream.Null);

    private static async Task ForwardDrainThenReturn502(HttpContext ctx)
    {
        await ctx.Request.Body.CopyToAsync(Stream.Null);
        ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
    }

    private static Task Return502BeforeReading(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
        return Task.CompletedTask;
    }

    private static async Task ForwardDrainThenThrow(HttpContext ctx)
    {
        await ctx.Request.Body.CopyToAsync(Stream.Null);
        throw new IOException("client reset after body sent");
    }

    private static Task RejectWithoutReading(HttpContext ctx, int status)
    {
        ctx.Response.StatusCode = status;
        return Task.CompletedTask;
    }

    private static DefaultHttpContext SeekableRequest(string body, string? contentType = "application/json")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "POST";
        if (contentType is not null) context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context;
    }

    private static DefaultHttpContext Request(string body, string? contentType = "application/json")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "POST";
        if (contentType is not null) context.Request.ContentType = contentType;
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
    public void ValidateConfig_FlatShape_Throws_WithMigrationHint()
    {
        var config = JsonDocument.Parse("""{ "maxSize": 1024 }""").RootElement;
        var ex = Assert.Throws<InvalidOperationException>(() => Build().Plugin.ValidateConfig(config));
        Assert.Contains("flat shape", ex.Message);
        Assert.Contains("request", ex.Message);
        Assert.Contains("response", ex.Message);
    }

    [Fact]
    public void ValidateConfig_NestedValidMaxSize_DoesNotThrow()
    {
        Build().Plugin.ValidateConfig(JsonDocument.Parse("""{ "request": { "maxSize": 1024 }, "response": { "maxSize": 2048 } }""").RootElement);
    }

    [Fact]
    public void ValidateConfig_MaxSizeAtCeiling_DoesNotThrow()
    {
        Build().Plugin.ValidateConfig(JsonDocument.Parse("""{ "request": { "maxSize": 32768 } }""").RootElement);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("response")]
    public void ValidateConfig_MaxSizeAboveCeiling_Throws(string direction)
    {
        var config = JsonDocument.Parse($$"""{ "{{direction}}": { "maxSize": 32769 } }""").RootElement;
        var ex = Assert.Throws<InvalidOperationException>(() => Build().Plugin.ValidateConfig(config));
        Assert.Contains("32768", ex.Message);
        Assert.Contains(direction, ex.Message);
    }

    [Fact]
    public void ValidateConfig_NonIntegerMaxSize_Throws()
    {
        var config = JsonDocument.Parse("""{ "response": { "maxSize": "big" } }""").RootElement;
        Assert.Throws<InvalidOperationException>(() => Build().Plugin.ValidateConfig(config));
    }

    [Fact]
    public void ValidateConfig_CeilingIsConfigurable_RaisedCeilingAcceptsLargerMaxSize()
    {
        var config = JsonDocument.Parse("""{ "request": { "maxSize": 49152 } }""").RootElement;

        var factory = LoggerFactory.Create(_ => { });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BodyCapture:MaxCaptureBytes"] = "65536" })
            .Build();

        var raised = new BodyCapturePlugin(factory, configuration);
        raised.ValidateConfig(config);

        Assert.Throws<InvalidOperationException>(() => Build().Plugin.ValidateConfig(config));
    }

    private static BodyCapturePlugin BuildWith(params (string Key, string Value)[] settings) =>
        new(LoggerFactory.Create(_ => { }),
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build());

    [Fact]
    public void Ctor_CeilingBeyondAddressableRange_ThrowsWithItsSize()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BuildWith(("BodyCapture:MaxCaptureBytes", (4L * 1024 * 1024 * 1024).ToString())));
        Assert.Contains("4096.0 MiB", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ClampsCaptureToCeiling_EvenIfValidateConfigWasSkipped()
    {
        var (plugin, logs) = Build();
        var context = Request(new string('z', 64 * 1024));

        var config = JsonDocument.Parse("""{ "request": { "maxSize": 1048576 } }""").RootElement;
        await plugin.ExecuteAsync(context, config, ForwardByDrainingBody);

        Assert.Contains("[Truncated by RequestBodyLogLimit]", logs.Text);
    }

    [Fact]
    public void ReadsRequestBody_IsFalse_SoRouteKeepsStreaming()
    {
        Assert.False(Build().Plugin.ReadsRequestBody);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesBody_AsItStreamsThrough()
    {
        var (plugin, logs) = Build();
        var context = Request("hello full body");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, ForwardByDrainingBody);

        Assert.Contains("hello full body", logs.Text);
    }

    [Fact]
    public async Task ExecuteAsync_TruncatesBody_WhenExceedsMaxSize()
    {
        var (plugin, logs) = Build();
        var context = Request("1234567890");

        var config = JsonDocument.Parse("""{ "request": { "maxSize": 5 } }""").RootElement;
        await plugin.ExecuteAsync(context, config, ForwardByDrainingBody);

        Assert.Contains("RequestBody: 12345", logs.Text);
        Assert.Contains("[Truncated by RequestBodyLogLimit]", logs.Text);
        Assert.DoesNotContain("67890", logs.Text);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsBodyIntact_ToTheUpstream()
    {
        var (plugin, _) = Build();

        var payload = Encoding.UTF8.GetBytes(new string('x', 200_000));
        var context = Request(new string('x', 200_000));

        var forwarded = new MemoryStream();
        var config = JsonDocument.Parse("""{ "request": { "maxSize": 64 } }""").RootElement;
        await plugin.ExecuteAsync(context, config, ctx => ctx.Request.Body.CopyToAsync(forwarded));

        Assert.Equal(payload, forwarded.ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_RestoresOriginalBodyStream_AfterForward()
    {
        var (plugin, _) = Build();
        var context = Request("body");
        var original = context.Request.Body;

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, ForwardByDrainingBody);

        Assert.Same(original, context.Request.Body);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotLogBody_ForUnrecognizedMediaType()
    {
        var (plugin, logs) = Build();
        var context = Request("1234567890", contentType: "application/octet-stream");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, ForwardByDrainingBody);

        Assert.DoesNotContain("1234567890", logs.Text);
        Assert.Contains("Unrecognized Content-Type", logs.Text);
    }

    [Fact]
    public async Task ExecuteAsync_LogsUnderPluginCategory_NotHttpLoggings()
    {
        var (plugin, logs) = Build();
        var context = Request("hello full body");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, ForwardByDrainingBody);

        Assert.Contains(typeof(BodyCapturePlugin).FullName!, logs.Categories);
        Assert.DoesNotContain(logs.Categories, c => c.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_BodiesSurvive_TheStockAspNetCoreWarningFilter()
    {
        var logs = new CapturedLogs();
        var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            b.AddProvider(logs);
        });
        var plugin = new BodyCapturePlugin(factory);

        await plugin.ExecuteAsync(Request("hello full body"), JsonDocument.Parse("""{"request":{}}""").RootElement, ForwardByDrainingBody);

        Assert.Contains("hello full body", logs.Text);
    }

    [Fact]
    public async Task ExecuteAsync_LogRecordCarriesRouteIdAndPath_AlongsideBody()
    {
        var (plugin, logs) = Build();
        var context = Request("attributed body");
        context.Items["ConduitSharp.RouteId"] = "route-orders";

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, ForwardByDrainingBody);

        var record = Assert.Single(logs.Records, r => r.Contains("attributed body"));
        Assert.Contains("route-orders", record);
        Assert.Contains("/api/test", record);
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task ExecuteAsync_ConcurrentRoutes_EachRecordPairsItsOwnRouteAndBody()
    {
        var (plugin, logs) = Build();
        var config = JsonDocument.Parse("""{"request":{}}""").RootElement;

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

    [Fact]
    public async Task TeePath_UpstreamReadsBodyThenReturns502_BodyIsCaptured_ItWasForwarded()
    {
        var (plugin, logs) = Build();
        var context = Request("tee: read then 502");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, ForwardDrainThenReturn502);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Contains("tee: read then 502", logs.Text);
    }

    [Fact]
    public async Task TeePath_UpstreamUnreachable_Returns502WithoutReadingBody_BodyNotCaptured_NotItsCause()
    {
        var (plugin, logs) = Build();
        var context = Request("tee: never read");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, Return502BeforeReading);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.DoesNotContain("tee: never read", logs.Text);
    }

    [Fact]
    public async Task ReusePath_UpstreamUnreachable_Returns502WithoutReadingBody_LogsAnyway_OverScopes()
    {
        var (plugin, logs) = Build();
        var context = SeekableRequest("reuse: logged though never forwarded");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, Return502BeforeReading);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Contains("reuse: logged though never forwarded", logs.Text);
    }

    [Fact]
    public async Task TeePath_ReadsBodyThenThrows_BodyCaptured_ModelsClientAbortNotUpstreamError()
    {
        var (plugin, logs) = Build();
        var context = Request("tee: read then abort");

        await Assert.ThrowsAsync<IOException>(() =>
            plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, ForwardDrainThenThrow));

        Assert.Contains("tee: read then abort", logs.Text);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task TeePath_LaterAuthRejects_BodyNotCaptured(int status)
    {
        var (plugin, logs) = Build();
        var context = Request($"tee: rejected {status}");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement,
            ctx => RejectWithoutReading(ctx, status));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.DoesNotContain($"tee: rejected {status}", logs.Text);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task ReusePath_OrderedBeforeAuth_RejectedBodyIsCaptured(int status)
    {
        var (plugin, logs) = Build();
        var context = SeekableRequest($"reuse: rejected {status}");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement,
            ctx => RejectWithoutReading(ctx, status));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Contains($"reuse: rejected {status}", logs.Text);
    }

    private static Task ForwardWritingResponse(HttpContext ctx, string body) => ctx.Response.WriteAsync(body);

    [Fact]
    public async Task Response_block_capturesResponseBody()
    {
        var (plugin, logs) = Build();
        var context = Request("req");
        var config = JsonDocument.Parse("""{ "response": { "maxSize": 4096 } }""").RootElement;

        await plugin.ExecuteAsync(context, config, ctx => ForwardWritingResponse(ctx, "the response payload"));

        var record = Assert.Single(logs.Records, r => r.Contains("Captured response body"));
        Assert.Contains("the response payload", record);
    }

    [Fact]
    public async Task Response_truncatesAtMaxSize()
    {
        var (plugin, logs) = Build();
        var context = Request("req");
        var config = JsonDocument.Parse("""{ "response": { "maxSize": 5 } }""").RootElement;

        await plugin.ExecuteAsync(context, config, ctx => ForwardWritingResponse(ctx, "1234567890"));

        var record = Assert.Single(logs.Records, r => r.Contains("Captured response body"));
        Assert.Contains("12345", record);
        Assert.Contains("(truncated)", record);
        Assert.DoesNotContain("67890", record);
    }

    [Fact]
    public async Task Response_capturesBinary_UnlikeTheRequestTee()
    {
        var (plugin, logs) = Build();
        var context = Request("req");
        var config = JsonDocument.Parse("""{ "response": { "maxSize": 16 } }""").RootElement;

        await plugin.ExecuteAsync(context, config, async ctx =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.WriteAsync("BINARYBYTES");
        });

        var record = Assert.Single(logs.Records, r => r.Contains("Captured response body"));
        Assert.Contains("BINARYBYTES", record);
    }

    [Fact]
    public async Task Response_isTransparent_ClientGetsEveryByte()
    {
        var (plugin, _) = Build();
        var context = Request("req");
        var sink = new MemoryStream();
        context.Response.Body = sink;
        var payload = new string('y', 200_000);
        var config = JsonDocument.Parse("""{ "response": { "maxSize": 64 } }""").RootElement;

        await plugin.ExecuteAsync(context, config, ctx => ForwardWritingResponse(ctx, payload));

        Assert.Equal(payload, Encoding.UTF8.GetString(sink.ToArray()));
        Assert.Same(sink, context.Response.Body);
    }

    [Fact]
    public async Task Request_and_response_bothCaptured_inOneConfig()
    {
        var (plugin, logs) = Build();
        var context = Request("the request body");
        var config = JsonDocument.Parse("""{ "request": { "maxSize": 4096 }, "response": { "maxSize": 4096 } }""").RootElement;

        await plugin.ExecuteAsync(context, config, async ctx =>
        {
            await ctx.Request.Body.CopyToAsync(Stream.Null);
            await ctx.Response.WriteAsync("the response body");
        });

        Assert.Contains("the request body", logs.Text);
        Assert.Contains("the response body", logs.Text);
        Assert.Single(logs.Records, r => r.Contains("Captured response body"));
    }

    [Fact]
    public async Task NoResponseBlock_doesNotCaptureResponse()
    {
        var (plugin, logs) = Build();
        var context = Request("req");

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{ "request": {} }""").RootElement,
            ctx => ForwardWritingResponse(ctx, "unwanted response"));

        Assert.DoesNotContain("Captured response body", logs.Text);
        Assert.DoesNotContain("unwanted response", logs.Text);
    }

    [Fact]
    public void CaptureMemoryBytes_sumsBothDirections()
    {
        var plugin = Build().Plugin;
        var config = JsonDocument.Parse("""{ "request": { "maxSize": 4096 }, "response": { "maxSize": 8192 } }""").RootElement;
        Assert.Equal(4096 + 8192, plugin.CaptureMemoryBytes(config));
    }

    [Fact]
    public void CaptureMemoryBytes_zeroWhenNoDirectionConfigured()
    {
        Assert.Equal(0, Build().Plugin.CaptureMemoryBytes(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void CaptureMemoryBytes_ceilingCapsEachDirection()
    {
        var config = JsonDocument.Parse("""{ "request": { "maxSize": 999999 }, "response": { "maxSize": 999999 } }""").RootElement;
        Assert.Equal(32 * 1024 + 32 * 1024, Build().Plugin.CaptureMemoryBytes(config));
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

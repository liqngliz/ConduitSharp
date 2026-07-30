using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace ConduitSharp.Plugin.BodyCaptureToFile.Tests;

public sealed class BodyCaptureToFilePluginTests : IDisposable
{
    private readonly string _tempLogFile;
    private readonly IConfiguration _configSub;

    public BodyCaptureToFilePluginTests()
    {
        _tempLogFile = Path.GetTempFileName();
        _configSub = Substitute.For<IConfiguration>();
        _configSub.GetSection("OTEL_BLRP_MAX_QUEUE_SIZE").Value.Returns("2048");
    }

    private BodyCaptureToFilePlugin Build(string? logPath = null)
    {
        var plugin = new BodyCaptureToFilePlugin(_configSub);
        
        var json = $$"""{ "logPath": "{{(logPath ?? _tempLogFile).Replace("\\", "\\\\")}}", "request": { "maxSize": 1024 } }""";
        var config = JsonDocument.Parse(json).RootElement;
        plugin.ValidateConfig(config);
        
        return plugin;
    }

    [Fact]
    public void CaptureMemoryBytes_DeclaresTheRentedBuffer_SoTheGatewayCanBudgetIt()
    {
        // The plugin rents a maxSize buffer per request and holds it until the background writer
        // drains the queue. Undeclared, that multiplies by concurrency with nothing to shed it —
        // declaring it puts the RAM under MaxRamBufferedBodyBytes and the gateway's 503.
        using var plugin = new BodyCaptureToFilePlugin(_configSub);
        var config = JsonDocument.Parse("""{ "request": { "maxSize": 8192 }, "response": { "maxSize": 4096 } }""").RootElement;

        Assert.Equal(8192 + 4096, plugin.CaptureMemoryBytes(config)); // sum of both directions
    }

    [Fact]
    public void CaptureMemoryBytes_WithoutMaxSize_DeclaresTheDefault_NotZero()
    {
        // Zero would mean "this plugin holds no memory of its own", which is exactly the silent
        // gap this closes: a route omitting maxSize still rents the 4 KiB default.
        using var plugin = new BodyCaptureToFilePlugin(_configSub);

        Assert.Equal(4 * 1024, plugin.CaptureMemoryBytes(JsonDocument.Parse("""{"request":{}}""").RootElement));
    }

    [Fact]
    public void ValidateConfig_ValidMaxSize_DoesNotThrow()
    {
        var json = $$"""{ "logPath": "{{_tempLogFile.Replace("\\", "\\\\")}}", "request": { "maxSize": 1024 } }""";
        var config = JsonDocument.Parse(json).RootElement;
        
        var plugin = new BodyCaptureToFilePlugin(_configSub);
        plugin.ValidateConfig(config); // Should not throw
    }

    [Fact]
    public void ValidateConfig_FlatShape_Throws_WithMigrationHint()
    {
        var config = JsonDocument.Parse("""{ "maxSize": 1024 }""").RootElement;
        using var plugin = new BodyCaptureToFilePlugin(_configSub);
        var ex = Assert.Throws<InvalidOperationException>(() => plugin.ValidateConfig(config));
        Assert.Contains("flat shape", ex.Message);
        Assert.Contains("request", ex.Message);
    }

    [Fact]
    public void ValidateConfig_NonIntegerMaxSize_Throws()
    {
        var config = JsonDocument.Parse("""{ "response": { "maxSize": "big" } }""").RootElement;
        using var plugin = new BodyCaptureToFilePlugin(_configSub);
        Assert.Throws<InvalidOperationException>(() => plugin.ValidateConfig(config));
    }

    [Fact]
    public async Task ExecuteAsync_CapturesFullBody_WrittenToDisk()
    {
        using var plugin = Build();
        
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("hello full body"));
        context.TraceIdentifier = "trace-123";

        var config = JsonDocument.Parse("""{"request":{}}""").RootElement;

        bool nextCalled = false;
        await plugin.ExecuteAsync(context, config, ctx => 
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);

        // Give the background thread a moment to flush
        await Task.Delay(100);

        var fileContents = await File.ReadAllLinesAsync(_tempLogFile);
        Assert.Single(fileContents);

        var logDoc = JsonDocument.Parse(fileContents[0]).RootElement;
        
        Assert.True(logDoc.TryGetProperty("time", out _));
        Assert.Equal("/api/test", logDoc.GetProperty("path").GetString());
        Assert.Equal("trace-123", logDoc.GetProperty("traceId").GetString());
        Assert.Equal("hello full body", logDoc.GetProperty("body").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_TruncatesBody_WrittenToDisk()
    {
        using var plugin = Build();
        
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test-trunc";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("1234567890"));
        context.TraceIdentifier = "trace-456";

        var config = JsonDocument.Parse("""{ "request": { "maxSize": 5 } }""").RootElement;

        await plugin.ExecuteAsync(context, config, _ => Task.CompletedTask);

        // Give the background thread a moment to flush
        await Task.Delay(100);

        var fileContents = await File.ReadAllLinesAsync(_tempLogFile);
        Assert.Single(fileContents);

        var logDoc = JsonDocument.Parse(fileContents[0]).RootElement;
        Assert.Equal("12345... (truncated)", logDoc.GetProperty("body").GetString());
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task ExecuteAsync_ConcurrentRequests_MultipleWrites()
    {
        using var plugin = Build();
        var config = JsonDocument.Parse("""{"request":{}}""").RootElement;

        var tasks = Enumerable.Range(0, 100).Select(async i =>
        {
            var context = new DefaultHttpContext();
            context.Request.Path = $"/api/req-{i}";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes($"body-{i}"));
            context.TraceIdentifier = $"trace-{i}";

            await plugin.ExecuteAsync(context, config, _ => Task.CompletedTask);
        });

        await Task.WhenAll(tasks);

        // Give the background thread time to flush all 100 requests
        await Task.Delay(250);

        var fileContents = await File.ReadAllLinesAsync(_tempLogFile);
        Assert.Equal(100, fileContents.Length);

        var parsedBodies = new HashSet<string>();
        foreach (var line in fileContents)
        {
            var logDoc = JsonDocument.Parse(line).RootElement;
            parsedBodies.Add(logDoc.GetProperty("body").GetString()!);
        }

        for (var i = 0; i < 100; i++)
        {
            Assert.Contains($"body-{i}", parsedBodies);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BoundsCapture_WhenNoMaxSizeConfigured()
    {
        // Omitting maxSize used to copy the whole body twice (MemoryStream + a pooled rent sized to
        // it), both outside the gateway's buffering budget. The default is what stops that.
        using var plugin = Build();

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/big";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 16 * 1024)));

        await plugin.ExecuteAsync(context, JsonDocument.Parse("""{"request":{}}""").RootElement, _ => Task.CompletedTask);

        await Task.Delay(100);

        var body = JsonDocument.Parse((await File.ReadAllLinesAsync(_tempLogFile))[0])
            .RootElement.GetProperty("body").GetString()!;

        Assert.EndsWith("... (truncated)", body);
        Assert.Equal(4 * 1024, body.Count(c => c == 'x'));
    }

    [Fact]
    public async Task ExecuteAsync_RollsFile_WhenMaxFileBytesExceeded()
    {
        // Without a roll the sink grows until the disk (or the tmpfs cap) stops it, and the writer
        // dies on ENOSPC with every request still succeeding. Tiny cap so a handful of entries trip it.
        var plugin = new BodyCaptureToFilePlugin(_configSub);
        var json = $$"""{ "logPath": "{{_tempLogFile.Replace("\\", "\\\\")}}", "request": { "maxSize": 1024 }, "maxFileBytes": 200 }""";
        plugin.ValidateConfig(JsonDocument.Parse(json).RootElement);

        using (plugin)
        {
            var config = JsonDocument.Parse("""{"request":{}}""").RootElement;
            for (var i = 0; i < 20; i++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = $"/api/roll-{i}";
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 100)));
                await plugin.ExecuteAsync(context, config, _ => Task.CompletedTask);
                await Task.Delay(15); // let the writer drain between entries so it can roll
            }

            await Task.Delay(250);
        }

        var backup = _tempLogFile + ".1";
        Assert.True(File.Exists(backup), "expected a rolled .1 backup once maxFileBytes was exceeded");
        // The live file is bounded by the roll — and right after one it may not exist at all until
        // the next entry recreates it (FileMode.Append). Either way it must not hold everything.
        var liveLength = File.Exists(_tempLogFile) ? new FileInfo(_tempLogFile).Length : 0;
        Assert.True(liveLength < 20 * 100, $"live file should be bounded by the roll, was {liveLength}");

        File.Delete(backup);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesResponseBody_WrittenToDisk_WithDirection()
    {
        using var plugin = Build();

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/resp";
        context.Request.Body = new MemoryStream(); // no request block, so request is not captured
        var sink = new MemoryStream();
        context.Response.Body = sink;

        var config = JsonDocument.Parse("""{ "response": { "maxSize": 4096 } }""").RootElement;
        await plugin.ExecuteAsync(context, config, ctx => ctx.Response.WriteAsync("the response payload"));

        await Task.Delay(100);

        var line = (await File.ReadAllLinesAsync(_tempLogFile)).Single();
        var doc = JsonDocument.Parse(line).RootElement;
        Assert.Equal("response", doc.GetProperty("direction").GetString());
        Assert.Equal("the response payload", doc.GetProperty("body").GetString());
        Assert.Equal("the response payload", Encoding.UTF8.GetString(sink.ToArray())); // client got every byte
    }

    [Fact]
    public async Task ExecuteAsync_CapturesBothDirections_TwoRecords()
    {
        using var plugin = Build();

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/both";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("the request body"));
        context.Response.Body = new MemoryStream();

        var config = JsonDocument.Parse("""{ "request": { "maxSize": 4096 }, "response": { "maxSize": 4096 } }""").RootElement;
        await plugin.ExecuteAsync(context, config, ctx => ctx.Response.WriteAsync("the response body"));

        await Task.Delay(100);

        var docs = (await File.ReadAllLinesAsync(_tempLogFile))
            .Select(l => JsonDocument.Parse(l).RootElement)
            .ToDictionary(d => d.GetProperty("direction").GetString()!, d => d.GetProperty("body").GetString());

        Assert.Equal("the request body",  docs["request"]);
        Assert.Equal("the response body", docs["response"]);
    }

    public void Dispose()
    {
        if (File.Exists(_tempLogFile))
        {
            File.Delete(_tempLogFile);
        }
    }
}

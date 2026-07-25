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
        
        var json = $$"""{ "logPath": "{{(logPath ?? _tempLogFile).Replace("\\", "\\\\")}}", "maxSize": 1024 }""";
        var config = JsonDocument.Parse(json).RootElement;
        plugin.ValidateConfig(config);
        
        return plugin;
    }

    [Fact]
    public void ValidateConfig_ValidMaxSize_DoesNotThrow()
    {
        var json = $$"""{ "logPath": "{{_tempLogFile.Replace("\\", "\\\\")}}", "maxSize": 1024 }""";
        var config = JsonDocument.Parse(json).RootElement;
        
        var plugin = new BodyCaptureToFilePlugin(_configSub);
        plugin.ValidateConfig(config); // Should not throw
    }

    [Fact]
    public void ValidateConfig_InvalidMaxSize_Throws()
    {
        var json = """{ "maxSize": -5 }""";
        var config = JsonDocument.Parse(json).RootElement;
        
        var plugin = new BodyCaptureToFilePlugin(_configSub);
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

        var config = JsonDocument.Parse("{}").RootElement;

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

        var config = JsonDocument.Parse("""{ "maxSize": 5 }""").RootElement;

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
        var config = JsonDocument.Parse("{}").RootElement;

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
    public async Task ExecuteAsync_RollsFile_WhenMaxFileBytesExceeded()
    {
        // Without a roll the sink grows until the disk (or the tmpfs cap) stops it, and the writer
        // dies on ENOSPC with every request still succeeding. Tiny cap so a handful of entries trip it.
        var plugin = new BodyCaptureToFilePlugin(_configSub);
        var json = $$"""{ "logPath": "{{_tempLogFile.Replace("\\", "\\\\")}}", "maxSize": 1024, "maxFileBytes": 200 }""";
        plugin.ValidateConfig(JsonDocument.Parse(json).RootElement);

        using (plugin)
        {
            var config = JsonDocument.Parse("{}").RootElement;
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

    public void Dispose()
    {
        if (File.Exists(_tempLogFile))
        {
            File.Delete(_tempLogFile);
        }
    }
}

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ConduitSharp.Plugin.BodyCapture.Tests;

public sealed class BodyCapturePluginTests
{
    private static BodyCapturePlugin Build(ILogger<BodyCapturePlugin>? logger = null) =>
        new(logger ?? Substitute.For<ILogger<BodyCapturePlugin>>());

    [Fact]
    public void ValidateConfig_ValidMaxSize_DoesNotThrow()
    {
        var json = """{ "maxSize": 1024 }""";
        var config = JsonDocument.Parse(json).RootElement;
        
        var plugin = Build();
        plugin.ValidateConfig(config); // Should not throw
    }

    [Fact]
    public void ValidateConfig_InvalidMaxSize_Throws()
    {
        var json = """{ "maxSize": -5 }""";
        var config = JsonDocument.Parse(json).RootElement;
        
        var plugin = Build();
        Assert.Throws<InvalidOperationException>(() => plugin.ValidateConfig(config));
    }

    [Fact]
    public async Task ExecuteAsync_CapturesFullBody_WhenNoMaxSize()
    {
        var logger = Substitute.For<ILogger<BodyCapturePlugin>>();
        var plugin = Build(logger);
        
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("hello full body"));
        
        var config = JsonDocument.Parse("{}").RootElement;

        bool nextCalled = false;
        Task Next(HttpContext ctx)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        await plugin.ExecuteAsync(context, config, Next);

        Assert.True(nextCalled);
        
        // NSubstitute check for LogInformation (which is an extension method over Log)
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("hello full body")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ExecuteAsync_TruncatesBody_WhenExceedsMaxSize()
    {
        var logger = Substitute.For<ILogger<BodyCapturePlugin>>();
        var plugin = Build(logger);
        
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("1234567890"));
        
        var config = JsonDocument.Parse("""{ "maxSize": 5 }""").RootElement;

        await plugin.ExecuteAsync(context, config, _ => Task.CompletedTask);

        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("12345... (truncated)")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    [Trait("Contract", "PluginIsolation")]
    public async Task ExecuteAsync_ConcurrentRequests_EachLogsItsOwnBody()
    {
        // One singleton plugin instance, many parallel requests — a shared-state bug
        // (cached body/config on the instance) would pair a path with another request's body.
        var lines  = new System.Collections.Concurrent.ConcurrentBag<string>();
        var logger = Substitute.For<ILogger<BodyCapturePlugin>>();
        logger.When(l => l.Log(
                Arg.Any<LogLevel>(), Arg.Any<EventId>(), Arg.Any<object>(),
                Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>()))
            .Do(call => lines.Add(call.ArgAt<object>(2).ToString()!));

        var plugin = new BodyCapturePlugin(logger);
        var config = JsonDocument.Parse("{}").RootElement;

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            var context = new DefaultHttpContext();
            context.Request.Path = $"/api/req-{i}";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes($"body-{i}"));

            await plugin.ExecuteAsync(context, config, _ => Task.CompletedTask);
        })));

        Assert.Equal(50, lines.Count);
        for (var i = 0; i < 50; i++)
            Assert.Contains(lines, l => l.Contains($"path /api/req-{i}: body-{i}"));
    }
}

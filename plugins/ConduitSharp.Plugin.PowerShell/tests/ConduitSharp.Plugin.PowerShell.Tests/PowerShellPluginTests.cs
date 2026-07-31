using ConduitSharp.Core.Routing;
using PS = System.Management.Automation.PowerShell;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace ConduitSharp.Plugin.PowerShell.Tests;

public sealed class PowerShellPluginTests
{
    private static PowerShellPlugin Build() =>
        new(Substitute.For<IConfiguration>());

    [Fact]
    public void Plugin_HasCorrectName()
    {
        Assert.Equal(PluginName.Custom, Build().Name);
        Assert.Equal("power-shell", Build().Variant);
    }

    [Fact]
    public void EmbeddedRuntime_CanExecuteScript()
    {
        using var ps = PS.Create();
        ps.AddScript("@{ ok = $true } | ConvertTo-Json -Compress");
        var results = ps.Invoke();

        Assert.False(ps.HadErrors,
            string.Join("\n", ps.Streams.Error.Select(e => e.ToString())));
        Assert.Single(results);
        Assert.Contains("\"ok\"", results[0].ToString());
    }

    [Fact]
    public async Task EmbeddedRuntime_CanRunErpReportScript()
    {
        var script = """
            $values = @(
                [PSCustomObject]@{ product = "Widget A"; value = 0.42 }
            )
            [PSCustomObject]@{ values = $values; averageValue = 0.42 } | ConvertTo-Json -Depth 5
            """;

        string output = "";
        await Task.Run(() =>
        {
            using var ps = PS.Create();
            ps.AddScript(script);
            var results = ps.Invoke();
            Assert.False(ps.HadErrors,
                string.Join("\n", ps.Streams.Error.Select(e => e.ToString())));
            output = string.Join("", results.Select(r => r?.ToString() ?? ""));
        });

        Assert.Contains("averageValue", output);
        Assert.Contains("Widget A", output);
    }

    [Fact]
    public async Task ExecuteAsync_100ConcurrentInvocations_AllComplete_NoThreadPoolStarvation()
    {
        var script = Path.Combine(Path.GetTempPath(), $"ps-load-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(script, "@{ ok = $true } | ConvertTo-Json -Compress");
        try
        {
            var plugin = Build();
            var config = System.Text.Json.JsonSerializer.SerializeToElement(new { scriptPath = script });

            var work = Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
            {
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                context.Response.Body = new MemoryStream();
                await plugin.ExecuteAsync(context, config, _ => Task.CompletedTask);
                return context.Response.StatusCode;
            })));

            var finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(120)));

            Assert.True(finished == work,
                "100 concurrent PowerShell invocations did not complete within 120s — thread-pool starvation.");
            Assert.All(await work, status => Assert.Equal(200, status));
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ClientAborts_ReturnsPromptly_DoesNotLeakThread()
    {
        var script = Path.Combine(Path.GetTempPath(), $"ps-hang-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(script, "Start-Sleep -Seconds 60; 'done'");
        try
        {
            var plugin = Build();
            var config = System.Text.Json.JsonSerializer.SerializeToElement(new { scriptPath = script });

            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            using var cts = new CancellationTokenSource();
            context.RequestAborted = cts.Token;
            cts.Cancel();

            var exec = plugin.ExecuteAsync(context, config, _ => Task.CompletedTask);
            var finished = await Task.WhenAny(exec, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(finished == exec,
                "ExecuteAsync did not return within 5s of RequestAborted firing — the hung script " +
                "is blocking a thread-pool thread until it completes on its own.");
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ScriptExceedsTimeout_Returns504()
    {
        var script = Path.Combine(Path.GetTempPath(), $"ps-slow-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(script, "Start-Sleep -Seconds 60; 'done'");
        try
        {
            var plugin = Build();
            var config = System.Text.Json.JsonSerializer.SerializeToElement(
                new { scriptPath = script, timeoutMs = 500 });

            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Response.Body = new MemoryStream();

            var exec = plugin.ExecuteAsync(context, config, _ => Task.CompletedTask);
            var finished = await Task.WhenAny(exec, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(finished == exec, "ExecuteAsync did not return within 5s — timeout not enforced.");
            await exec;
            Assert.Equal(504, context.Response.StatusCode);
        }
        finally
        {
            File.Delete(script);
        }
    }
}

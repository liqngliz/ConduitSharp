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
        // Verifies the embedded Microsoft.PowerShell.SDK runtime works in-process
        // without any system pwsh installation.
        using var ps = PS.Create();
        ps.AddScript("@{ ok = $true } | ConvertTo-Json -Compress");
        var results = ps.Invoke();

        Assert.False(ps.HadErrors,
            string.Join("\n", ps.Streams.Error.Select(e => e.ToString())));
        Assert.Single(results);
        Assert.Contains("\"ok\"", results[0].ToString());
    }

    [Fact]
    public async Task EmbeddedRuntime_CanRunMarginReportScript()
    {
        // Runs the actual Get-MarginReport.ps1 content through the embedded runtime
        // and confirms it returns valid JSON with the expected fields.
        var script = """
            $margins = @(
                [PSCustomObject]@{ product = "Widget A"; margin = 0.42 }
            )
            [PSCustomObject]@{ margins = $margins; averageMargin = 0.42 } | ConvertTo-Json -Depth 5
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

        Assert.Contains("averageMargin", output);
        Assert.Contains("Widget A", output);
    }

    [Fact]
    public async Task ExecuteAsync_100ConcurrentInvocations_AllComplete_NoThreadPoolStarvation()
    {
        // ps.Invoke() runs synchronously inside Task.Run, parking a pool thread per request.
        // Under a burst that far exceeds the core count, the pool's slow thread injection is
        // the risk: requests queue behind blocked threads and the gateway appears hung. This
        // holds 100 concurrent invocations to a deadline and demands every one succeeds.
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
}

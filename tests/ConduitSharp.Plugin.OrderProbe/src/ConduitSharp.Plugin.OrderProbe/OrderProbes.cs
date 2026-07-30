using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConduitSharp.Plugin.OrderProbe;

/// <summary>
/// Logs <c>enter</c> before <c>next()</c> and <c>exit</c> after, through the host's real
/// <see cref="ILoggerFactory"/> under category <c>Probe.{id}</c>. The plugin-chain-order E2E reads
/// execution order from those actual process log lines. Each variant is a distinct concrete type
/// because the plugin folder scanner registers implementations by type.
/// </summary>
public abstract class OrderProbeBase : IPipelinePlugin
{
    protected abstract string ProbeId { get; }

    public PluginName Name => PluginName.Custom;
    public string? Variant => $"probe-{ProbeId}";
    public string Id => $"probe-{ProbeId}";

    public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
    {
        var log = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger($"Probe.{ProbeId}");

        log.LogInformation("enter");
        await next(context);
        log.LogInformation("exit");
    }
}

public sealed class OrderProbeA : OrderProbeBase { protected override string ProbeId => "a"; }
public sealed class OrderProbeB : OrderProbeBase { protected override string ProbeId => "b"; }
public sealed class OrderProbeC : OrderProbeBase { protected override string ProbeId => "c"; }
public sealed class OrderProbeE : OrderProbeBase { protected override string ProbeId => "e"; }

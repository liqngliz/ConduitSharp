using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ConduitSharp.Core.Routing;
namespace ConduitSharp.Core.Pipeline;

/// <summary>
/// Contract every plugin must implement — built-in or dynamically loaded.
///
/// Execution model (middleware pattern):
///   • Do pre-processing work (auth check, rate limit, etc.)
///   • Call <c>next(context)</c> to forward to the next plugin in the chain.
///   • Optionally do post-processing after <c>next</c> returns (e.g. cache the response).
///   • To short-circuit (block the request), simply set response status code and return WITHOUT calling <c>next</c>.
/// </summary>
public interface IPipelinePlugin
{
    /// <summary>Identifies this plugin; must match a <see cref="PluginName"/> enum value.</summary>
    PluginName Name { get; }

    /// <summary>
    /// Stable string identity for this plugin, used as the registry key. External plugins
    /// declare this once and never change it — the numeric `PluginName` enum can be freely
    /// reordered or deleted without breaking published plugin binaries. E.g. "http-proxy",
    /// "cache", "rate-limit". Must be lowercase and unique per plugin.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Disambiguator for plugins that share <see cref="PluginName.Custom"/> — the self-chosen
    /// name a premium/drop-in plugin registers under (e.g. <c>"llm-proxy"</c>). Built-in plugins
    /// leave this null. Routes select a variant plugin with <c>"name": "custom", "variant": "…"</c>.
    /// </summary>
    string? Variant => null;

    /// <summary>
    /// Executes the plugin's logic. Call <paramref name="next"/> to continue the chain,
    /// or write to the response and return without calling <paramref name="next"/>
    /// to block the request.
    /// </summary>
    Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next);

    /// <summary>
    /// Validates this plugin's per-route <c>config</c> block, throwing on invalid config.
    /// Called once per route at startup (and on admin reload) so config errors fail fast
    /// rather than surfacing on the first request. Default: no validation.
    /// </summary>
    void ValidateConfig(JsonElement config) { }

    /// <summary>
    /// True if this plugin reads the request body (to inspect, hash, capture, etc.). Such a plugin
    /// needs the buffered, rewindable body the gateway provides by default — so declaring a route
    /// <c>streamOnly</c> while it carries a body-reading plugin is rejected at startup, rather than
    /// silently handing the plugin a forward-only stream it would consume before the forward.
    /// Default: false.
    /// </summary>
    bool ReadsRequestBody => false;
}

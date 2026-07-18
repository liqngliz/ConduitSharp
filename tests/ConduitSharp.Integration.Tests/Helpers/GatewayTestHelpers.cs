using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;

namespace ConduitSharp.Integration.Tests.Helpers;

internal static class GatewayTestHelpers
{
    internal static string CatchAllRoutes(
        string upstreamBaseUrl,
        string lbStrategy = "RoundRobin",
        string? upstreamOverride = null) => $$"""
        {
          "routes": [{
            "id": "test-route",
            "route": { "match": { "path": "/{**rest}" } },
            "cluster": {
              "loadBalancingPolicy": "{{lbStrategy}}",
              "destinations": { "node-0": { "address": "{{upstreamOverride ?? upstreamBaseUrl}}" } },
              "httpRequest": { "activityTimeout": "00:00:05" }
            },
            "plugins": []
          }]
        }
        """;

    internal static string RouteWithPlugin(
        string upstreamBaseUrl,
        string pluginName,
        object pluginConfig) => $$"""
        {
          "routes": [{
            "id": "test-route",
            "route": { "match": { "path": "/{**rest}" } },
            "cluster": {
              "loadBalancingPolicy": "RoundRobin",
              "destinations": { "node-0": { "address": "{{upstreamBaseUrl}}" } },
              "httpRequest": { "activityTimeout": "00:00:05" }
            },
            "plugins": [{
              "name": "{{pluginName}}",
              "order": 1,
              "enabled": true,
              "config": {{JsonSerializer.Serialize(pluginConfig)}}
            }]
          }]
        }
        """;
    /// <summary>
    /// One route per config: ids <c>route-a</c>…, paths <c>/a/{**rest}</c>…, each carrying
    /// the same plugin with its own config. Used to prove per-route plugin configs stay isolated.
    /// </summary>
    internal static string RoutesWithPlugin(
        string upstreamBaseUrl,
        string pluginName,
        params object[] pluginConfigs) =>
        RoutesWithPlugin(upstreamBaseUrl, pluginName,
            pluginConfigs.Select(c => ((string?)null, c)).ToArray());

    /// <summary>Tuple overload for custom plugins that need a per-route <c>variant</c>.</summary>
    internal static string RoutesWithPlugin(
        string upstreamBaseUrl,
        string pluginName,
        params (string? Variant, object Config)[] plugins)
    {
        var routes = plugins.Select((p, i) =>
        {
            var id = (char)('a' + i);
            var variant = p.Variant is null ? "" : $"\"variant\": \"{p.Variant}\",";
            return $$"""
            {
              "id": "route-{{id}}",
              "route": { "match": { "path": "/{{id}}/{**rest}" } },
              "cluster": {
                "loadBalancingPolicy": "RoundRobin",
                "destinations": { "node-0": { "address": "{{upstreamBaseUrl}}" } },
                "httpRequest": { "activityTimeout": "00:00:05" }
              },
              "plugins": [{
                "name": "{{pluginName}}",
                {{variant}}
                "order": 1,
                "enabled": true,
                "config": {{JsonSerializer.Serialize(p.Config)}}
              }]
            }
            """;
        });
        return $$"""{ "routes": [{{string.Join(",", routes)}}] }""";
    }
}

/// <summary>
/// Stub plugin that short-circuits every request with a fixed status, body,
/// and optional response headers. Used to test the plugin pipeline in isolation.
/// </summary>
internal sealed class FixedStatusPlugin(
    PluginName name,
    int statusCode,
    string? body = null,
    Dictionary<string, string>? headers = null) : IPipelinePlugin
{
    public PluginName Name => name;
    public string Id => name.ToString().ToLowerInvariant().Replace('_', '-');

    public Task ExecuteAsync(HttpContext context, System.Text.Json.JsonElement config, RequestDelegate next)
    {
        if (headers is not null)
            foreach (var (k, v) in headers)
                context.Response.Headers[k] = v;

        context.Response.StatusCode = statusCode; if (body != null) { context.Response.WriteAsync(body).GetAwaiter().GetResult(); }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stub plugin that throws from <see cref="ExecuteAsync"/>, simulating a buggy plugin.
/// Used to verify the gateway converts unhandled plugin exceptions into a 500.
/// </summary>
internal sealed class ThrowingPlugin(PluginName name) : IPipelinePlugin
{
    public PluginName Name => name;
    public string Id => name.ToString().ToLowerInvariant().Replace('_', '-');

    public Task ExecuteAsync(HttpContext context, System.Text.Json.JsonElement config, RequestDelegate next) =>
        throw new InvalidOperationException("boom from plugin");
}

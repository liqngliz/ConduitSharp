using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Transformation.Plugins;

/// <summary>
/// Adds, removes, and rewrites HTTP request headers before the request is forwarded upstream.
///
/// routes.json config block:
/// <code>
/// {
///   "add":    { "X-Request-Id": "static-value", "X-Source": "gateway" },
///   "set":    { "X-Forwarded-By": "conduit" },
///   "remove": [ "X-Internal-Token", "X-Debug" ]
/// }
/// </code>
/// <list type="bullet">
///   <item><c>add</c>    — adds the header only if it is not already present.</item>
///   <item><c>set</c>    — adds or overwrites the header unconditionally.</item>
///   <item><c>remove</c> — removes the header if present (case-insensitive).</item>
/// </list>
/// </summary>
public sealed class HeaderTransformPlugin : IPipelinePlugin
{
    public PluginName Name => PluginName.HeaderTransform;
    public string Id => Name.ToId();

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        var config  = HeaderTransformConfig.From(configElement);
        var headers = context.Request.Headers;

        foreach (var name in config.Remove)
        {
            headers.Remove(name);
        }

        foreach (var (key, value) in config.Add)
        {
            if (!headers.ContainsKey(key))
                headers[key] = value;
        }

        foreach (var (key, value) in config.Set)
            headers[key] = value;

        await next(context);
    }
}

/// <summary>
/// Configuration for the <c>header-transform</c> plugin.
/// Mutates request headers before forwarding to the upstream. Three independent operations
/// are applied in order: <c>remove</c> first, then <c>set</c>, then <c>add</c>.
/// Place inside the route's <c>"config"</c> block.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>remove</c> — deletes the named header if present; no-op if absent.</item>
///   <item><c>set</c>    — overwrites the header value, or creates it if absent.</item>
///   <item><c>add</c>    — appends a new header; does not overwrite an existing one.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// {
///   "name": "header-transform",
///   "order": 2,
///   "enabled": true,
///   "config": {
///     "remove": ["X-Internal-Debug"],
///     "set":    { "X-Forwarded-By": "ConduitSharp", "X-Environment": "production" },
///     "add":    { "X-Request-Id": "generated-by-upstream" }
///   }
/// }
/// </code>
/// </example>
public sealed record HeaderTransformConfig
{
    /// <summary>Headers to add. Does not overwrite an existing header with the same name.</summary>
    [JsonPropertyName("add")]    public Dictionary<string, string> Add    { get; init; } = [];

    /// <summary>Headers to set. Creates the header if absent, overwrites if present.</summary>
    [JsonPropertyName("set")]    public Dictionary<string, string> Set    { get; init; } = [];

    /// <summary>Header names to remove before forwarding.</summary>
    [JsonPropertyName("remove")] public List<string>               Remove { get; init; } = [];

    internal static HeaderTransformConfig From(JsonElement raw) =>
        raw.Deserialize<HeaderTransformConfig>(JsonOptions)
        ?? throw new InvalidOperationException("header-transform plugin config is null or invalid.");

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

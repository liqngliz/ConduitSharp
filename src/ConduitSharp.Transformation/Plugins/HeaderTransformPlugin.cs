using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Transformation.Plugins;

/// <summary>
/// Adds, removes, and rewrites HTTP headers on the request (before it is forwarded upstream) and on
/// the response (before it is sent to the client). Direction is chosen by which block the operation
/// sits in.
///
/// routes.json config block:
/// <code>
/// {
///   "request":  { "add": { "X-Source": "gateway" }, "set": { "X-Forwarded-By": "conduit" }, "remove": [ "X-Internal-Secret" ] },
///   "response": { "remove": [ "Server", "X-Powered-By" ] }
/// }
/// </code>
/// Within a block, operations apply in a fixed order: <c>remove</c>, then <c>add</c> (adds only if
/// absent), then <c>set</c> (adds or overwrites). Either block may be omitted.
///
/// Response headers are applied from <see cref="HttpResponse.OnStarting"/>, which fires just before
/// the headers flush. A note on <c>Server</c>: Kestrel writes its own <c>Server</c> header during
/// flush unless <c>AddServerHeader=false</c> on the host, so removing <c>Server</c> here strips the
/// upstream's copy but may not stop Kestrel adding one back. Disable it at the Kestrel level if that
/// matters.
/// </summary>
public sealed class HeaderTransformPlugin : IPipelinePlugin
{
    public PluginName Name => PluginName.HeaderTransform;
    public string Id => Name.ToId();

    public void ValidateConfig(JsonElement configElement)
    {
        if (configElement.ValueKind == JsonValueKind.Object
            && (configElement.TryGetProperty("add", out _)
                || configElement.TryGetProperty("set", out _)
                || configElement.TryGetProperty("remove", out _)))
        {
            throw new InvalidOperationException(
                "header-transform config uses the removed flat shape ({ \"add\", \"set\", \"remove\" }). " +
                "Nest operations under 'request' and/or 'response', e.g. " +
                "{ \"request\": { \"remove\": [\"X-Debug\"] }, \"response\": { \"remove\": [\"Server\"] } }.");
        }

        _ = HeaderTransformConfig.From(configElement);
    }

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        var config = HeaderTransformConfig.From(configElement);

        Apply(config.Request, context.Request.Headers);

        if (config.Response.HasWork)
        {
            context.Response.OnStarting(() =>
            {
                Apply(config.Response, context.Response.Headers);
                return Task.CompletedTask;
            });
        }

        await next(context);
    }

    private static void Apply(HeaderOperations ops, IHeaderDictionary headers)
    {
        foreach (var name in ops.Remove)
            headers.Remove(name);

        foreach (var (key, value) in ops.Add)
            if (!headers.ContainsKey(key))
                headers[key] = value;

        foreach (var (key, value) in ops.Set)
            headers[key] = value;
    }
}

/// <summary>
/// Configuration for the <c>header-transform</c> plugin. Operations are split by direction:
/// <see cref="Request"/> mutates headers before the upstream call, <see cref="Response"/> mutates
/// them before the reply reaches the client. Place inside the route's <c>"config"</c> block.
/// </summary>
/// <example>
/// <code>
/// {
///   "name": "header-transform",
///   "order": 2,
///   "config": {
///     "request":  { "remove": ["X-Internal-Debug"], "set": { "X-Forwarded-By": "ConduitSharp" } },
///     "response": { "remove": ["Server", "X-Powered-By"] }
///   }
/// }
/// </code>
/// </example>
public sealed record HeaderTransformConfig
{
    /// <summary>Header operations applied to the outgoing upstream request.</summary>
    [JsonPropertyName("request")]  public HeaderOperations Request  { get; init; } = new();

    /// <summary>Header operations applied to the response before it is sent to the client.</summary>
    [JsonPropertyName("response")] public HeaderOperations Response { get; init; } = new();

    internal static HeaderTransformConfig From(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object
            ? raw.Deserialize<HeaderTransformConfig>(JsonOptions) ?? new HeaderTransformConfig()
            : new HeaderTransformConfig();

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

/// <summary>
/// One direction's header operations. Applied in order: <c>remove</c>, then <c>add</c> (only if the
/// header is absent), then <c>set</c> (creates or overwrites).
/// </summary>
public sealed record HeaderOperations
{
    /// <summary>Headers to add only if not already present.</summary>
    [JsonPropertyName("add")]    public Dictionary<string, string> Add    { get; init; } = [];

    /// <summary>Headers to set unconditionally (creates if absent, overwrites if present).</summary>
    [JsonPropertyName("set")]    public Dictionary<string, string> Set    { get; init; } = [];

    /// <summary>Header names to remove (case-insensitive).</summary>
    [JsonPropertyName("remove")] public List<string>               Remove { get; init; } = [];

    /// <summary>True when this block has any operation, so the response path can skip a no-op OnStarting.</summary>
    [JsonIgnore] public bool HasWork => Add.Count > 0 || Set.Count > 0 || Remove.Count > 0;
}

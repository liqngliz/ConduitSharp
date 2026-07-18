using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Security.ApiKey;

/// <summary>
/// Validates an API key carried in a request header (default: <c>X-Api-Key</c>).
/// Short-circuits with 401 if the key is absent or not in the allow-list.
///
/// routes.json config block:
/// <code>
/// {
///   "header": "X-Api-Key",
///   "keys":   ["key-one", "key-two"]
/// }
/// </code>
/// </summary>
public sealed class ApiKeyAuthPlugin : IPipelinePlugin
{
    public PluginName Name => PluginName.ApiKeyAuth;
    public string Id => Name.ToId();

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        var config = ApiKeyAuthConfig.From(configElement);

        if (!context.Request.Headers.TryGetValue(config.Header, out var suppliedKey) ||
            string.IsNullOrWhiteSpace(suppliedKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync($"API key header '{config.Header}' is required.");
            return;
        }

        if (!ApiKeyAuthHandler.IsValid(suppliedKey.ToString(), config.Keys))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid API key.");
            return;
        }

        await next(context);
    }
}

/// <summary>
/// Configuration for the <c>api-key-auth</c> plugin.
/// Validates a request header against a list of plain-text API keys.
/// Use <c>api-key-auth-hashed</c> instead if you do not want raw keys stored in config.
/// Place inside the route's <c>"config"</c> block.
/// </summary>
/// <example>
/// <code>
/// {
///   "name": "api-key-auth",
///   "order": 1,
///   "enabled": true,
///   "config": {
///     "header": "X-Api-Key",
///     "keys": [
///       "key-one-abc123",
///       "key-two-def456"
///     ]
///   }
/// }
/// </code>
/// </example>
public sealed record ApiKeyAuthConfig
{
    /// <summary>Request header to read the API key from. Default: <c>"X-Api-Key"</c>.</summary>
    [JsonPropertyName("header")] public string Header { get; init; } = "X-Api-Key";

    /// <summary>Accepted plain-text API keys. At least one must be provided.</summary>
    [JsonPropertyName("keys")]   public IReadOnlyList<string> Keys { get; init; } = [];

    internal static ApiKeyAuthConfig From(JsonElement raw) =>
        raw.Deserialize<ApiKeyAuthConfig>(JsonOptions)
        ?? throw new InvalidOperationException("api-key-auth plugin config is null or invalid.");

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

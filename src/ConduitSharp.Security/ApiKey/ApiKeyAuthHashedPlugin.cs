using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Security.ApiKey;

/// <summary>
/// Validates an API key by comparing its SHA-256 hash against stored hashes.
/// Use this instead of <see cref="ApiKeyAuthPlugin"/> when you do not want raw
/// keys stored in routes.json.
///
/// The <c>keys</c> array must contain lowercase hex-encoded SHA-256 hashes:
/// <code>
/// {
///   "header": "X-Api-Key",
///   "keys": [
///     "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
///   ]
/// }
/// </code>
/// To generate a hash: <c>echo -n "my-key" | sha256sum</c>
/// </summary>
public sealed class ApiKeyAuthHashedPlugin : IPipelinePlugin
{
    public PluginName Name => PluginName.ApiKeyAuthHashed;
    public string Id => Name.ToId();

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        var config = ApiKeyAuthHashedConfig.From(configElement);

        if (!context.Request.Headers.TryGetValue(config.Header, out var suppliedKey) ||
            string.IsNullOrWhiteSpace(suppliedKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync($"API key header '{config.Header}' is required.");
            return;
        }

        if (!ApiKeyAuthHashedHandler.IsValid(suppliedKey.ToString(), config.Keys))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid API key.");
            return;
        }

        await next(context);
    }
}

/// <summary>
/// Configuration for the <c>api-key-auth-hashed</c> plugin.
/// Validates a request header against SHA-256 hashes of API keys — raw keys are never stored.
/// Generate a hash with: <c>echo -n "my-key" | sha256sum</c> (Linux) or
/// <c>[System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes("my-key"))).ToLower()</c> (PowerShell).
/// Place inside the route's <c>"config"</c> block.
/// </summary>
/// <example>
/// <code>
/// {
///   "name": "api-key-auth-hashed",
///   "order": 1,
///   "enabled": true,
///   "config": {
///     "header": "X-Api-Key",
///     "keys": [
///       "b94f6f125c79e3a5ffaa826f584c10d52ada669e6762051b826b55776d05a8d",
///       "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
///     ]
///   }
/// }
/// </code>
/// </example>
public sealed record ApiKeyAuthHashedConfig
{
    /// <summary>Request header to read the API key from. Default: <c>"X-Api-Key"</c>.</summary>
    [JsonPropertyName("header")] public string Header { get; init; } = "X-Api-Key";

    /// <summary>Lowercase hex-encoded SHA-256 hashes of valid API keys.</summary>
    [JsonPropertyName("keys")] public IReadOnlyList<string> Keys { get; init; } = [];

    internal static ApiKeyAuthHashedConfig From(JsonElement raw) =>
        raw.Deserialize<ApiKeyAuthHashedConfig>(JsonOptions)
        ?? throw new InvalidOperationException("api-key-auth-hashed plugin config is null or invalid.");

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

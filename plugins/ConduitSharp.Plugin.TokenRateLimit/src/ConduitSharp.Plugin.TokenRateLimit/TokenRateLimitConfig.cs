using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConduitSharp.Plugin.TokenRateLimit;

/// <summary>Configuration for the <c>token-rate-limit</c> plugin.</summary>
public sealed record TokenRateLimitConfig
{
    /// <summary>Token budget per window, per caller. Required.</summary>
    [JsonPropertyName("maxTokensPerWindow")] public long MaxTokensPerWindow { get; init; }

    /// <summary>Fixed window length in seconds. Default 60.</summary>
    [JsonPropertyName("windowSeconds")] public int WindowSeconds { get; init; } = 60;

    /// <summary>Header whose value keys the budget per caller (e.g. <c>X-Api-Key</c>).</summary>
    [JsonPropertyName("keyHeader")] public string? KeyHeader { get; init; }

    /// <summary>JWT claim (from the Authorization Bearer token) that keys the budget per caller
    /// (e.g. <c>sub</c>). Used when <see cref="KeyHeader"/> is absent. The token is not re-validated;
    /// an upstream jwt-auth plugin already did.</summary>
    [JsonPropertyName("keyClaim")] public string? KeyClaim { get; init; }

    /// <summary>Dotted JSON paths in the response body whose numeric values are summed as the token
    /// cost, e.g. <c>["usage.prompt_tokens", "usage.completion_tokens"]</c>. Required.</summary>
    [JsonPropertyName("usageFields")] public IReadOnlyList<string> UsageFields { get; init; } = [];

    /// <summary>Cap on the response bytes buffered to find the usage fields. Default 1 MiB.</summary>
    [JsonPropertyName("maxResponseBytes")] public int MaxResponseBytes { get; init; } = 1024 * 1024;

    internal static TokenRateLimitConfig From(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object
            ? raw.Deserialize<TokenRateLimitConfig>(JsonOptions) ?? new TokenRateLimitConfig()
            : new TokenRateLimitConfig();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Traffic.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ConduitSharp.Plugin.TokenRateLimit;

/// <summary>
/// Meters LLM token usage per caller against a fixed-window budget, reusing the gateway's shared
/// <see cref="IRateLimitStore"/> (in-memory, or the Redis drop-in for a budget shared across
/// replicas). Unlike request-count limiting, the cost of a call is unknown until the model answers,
/// so this is <b>charge-after</b>: it reads the token counts from the response body and adds them to
/// the window's counter; the next request that finds the window already over budget gets a 429. A
/// request therefore overshoots the budget by at most its own cost.
///
/// <para>Because the usage fields live in the body, the whole response is buffered so it can be
/// parsed. The buffer is write-through, so the client still receives the response as it streams (the
/// gateway keeps a copy to parse afterward), and it is bounded by <c>maxResponseBytes</c> and reserved
/// against the RAM budget through <see cref="CaptureMemoryBytes"/>.</para>
///
/// <para>Which fields hold the token counts is config, so one plugin covers every provider: OpenAI
/// <c>usage.prompt_tokens</c> + <c>usage.completion_tokens</c>, Anthropic <c>usage.input_tokens</c> +
/// <c>usage.output_tokens</c>, Gemini <c>usageMetadata.totalTokenCount</c>, Ollama
/// <c>prompt_eval_count</c> + <c>eval_count</c>. The listed fields are summed.</para>
///
/// <para>Streaming (SSE) bodies are not a single JSON document, so usage parses as 0 and the call goes
/// uncounted; front the model's non-streaming endpoint, or add a per-provider SSE reader.</para>
///
/// routes.json config, variant <c>token-rate-limit</c>:
/// <code>
/// { "maxTokensPerWindow": 100000, "windowSeconds": 60, "keyClaim": "sub",
///   "usageFields": ["usage.prompt_tokens", "usage.completion_tokens"] }
/// </code>
/// </summary>
public sealed class TokenRateLimitPlugin : IPipelinePlugin
{
    public PluginName Name => PluginName.Custom;
    public string? Variant => "token-rate-limit";
    public string Id => "token-rate-limit";
    public bool ReadsRequestBody => false;

    /// <summary>The response buffer this plugin holds per request, reserved against the RAM budget.</summary>
    public int CaptureMemoryBytes(JsonElement config) => TokenRateLimitConfig.From(config).MaxResponseBytes;

    public void ValidateConfig(JsonElement config)
    {
        var c = TokenRateLimitConfig.From(config);
        if (c.MaxTokensPerWindow <= 0)
            throw new InvalidOperationException($"token-rate-limit: 'maxTokensPerWindow' must be greater than zero (was {c.MaxTokensPerWindow}).");
        if (c.WindowSeconds <= 0)
            throw new InvalidOperationException($"token-rate-limit: 'windowSeconds' must be greater than zero (was {c.WindowSeconds}).");
        if (c.UsageFields.Count == 0)
            throw new InvalidOperationException("token-rate-limit: 'usageFields' must list at least one field holding a token count, e.g. [\"usage.total_tokens\"].");
        if (c.MaxResponseBytes <= 0)
            throw new InvalidOperationException($"token-rate-limit: 'maxResponseBytes' must be greater than zero (was {c.MaxResponseBytes}).");
    }

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        var config = TokenRateLimitConfig.From(configElement);

        var store = context.RequestServices.GetRequiredService<IRateLimitStore>();
        var routeId = context.Items.TryGetValue("ConduitSharp.RouteId", out var r) && r is string id ? id : "";
        var clientKey = ResolveClientKey(context, config);
        var key = $"{routeId}\0{clientKey}";

        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowId = nowSeconds / config.WindowSeconds;

        if (store.Peek(key, windowId) >= config.MaxTokensPerWindow)
        {
            context.Response.Headers["Retry-After"] = (config.WindowSeconds - nowSeconds % config.WindowSeconds).ToString();
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Token rate limit exceeded. Try again later.");
            return;
        }

        var originalBody = context.Response.Body;
        using var capture = new BoundedTeeStream(originalBody, config.MaxResponseBytes);
        context.Response.Body = capture;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var tokens = ExtractTokens(capture.Captured.Span, config.UsageFields);
        if (tokens > 0)
            store.Add(key, windowId, config.WindowSeconds, tokens);
    }

    private static string ResolveClientKey(HttpContext context, TokenRateLimitConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.KeyHeader)
            && context.Request.Headers.TryGetValue(config.KeyHeader, out var h)
            && !string.IsNullOrWhiteSpace(h.ToString()))
            return h.ToString();

        if (!string.IsNullOrWhiteSpace(config.KeyClaim)
            && ClaimFromBearer(context, config.KeyClaim!) is { } claim)
            return claim;

        return "global";
    }

    private static string? ClaimFromBearer(HttpContext context, string claim)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = header[prefix.Length..].Split('.');
        if (parts.Length < 2)
            return null;

        try
        {
            var payload = Convert.FromBase64String(PadBase64Url(parts[1]));
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty(claim, out var value))
                return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string PadBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
    }

    private static long ExtractTokens(ReadOnlySpan<byte> body, IReadOnlyList<string> fields)
    {
        if (body.IsEmpty)
            return 0;
        try
        {
            using var doc = JsonDocument.Parse(body.ToArray());
            long sum = 0;
            foreach (var path in fields)
                if (TryGetByPath(doc.RootElement, path, out var value))
                    sum += value;
            return sum;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static bool TryGetByPath(JsonElement root, string path, out long value)
    {
        value = 0;
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return false;
        }
        return current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out value);
    }
}

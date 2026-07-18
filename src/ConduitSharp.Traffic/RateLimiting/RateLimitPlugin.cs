using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ConduitSharp.Traffic.RateLimiting;

/// <summary>
/// Applies rate limiting per route and per client key, short-circuiting with 429 when the limit is
/// exceeded. The algorithm is whatever <see cref="IRateLimiter"/> is registered — fixed-window by
/// default, or a drop-in from the plugins directory (see the SlidingWindow example).
///
/// routes.json config block:
/// <code>
/// {
///   "windowSeconds": 60,
///   "maxRequests":   100,
///   "keyHeader":     "X-Client-Id"   // optional; defaults to per-route global counter
/// }
/// </code>
/// </summary>
public sealed class RateLimitPlugin : IPipelinePlugin
{
    // Resolved once: IRateLimiter is a singleton and takes window/quota per call, so one instance
    // serves every route and there is nothing to cache per config.
    private readonly IRateLimiter _limiter;

    public RateLimitPlugin() => _limiter = new FixedWindowRateLimiter();

    // DI constructor: picks up a drop-in IRateLimiter (algorithm) and, through it, a drop-in
    // IRateLimitStore (counter backend, e.g. Redis).
    public RateLimitPlugin(IServiceProvider serviceProvider) =>
        _limiter = serviceProvider.GetService<IRateLimiter>() ?? new FixedWindowRateLimiter();

    public PluginName Name => PluginName.RateLimit;
    public string Id => Name.ToId();

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        var config = RateLimitConfig.From(configElement);
        var routeId = (string)context.Items["ConduitSharp.RouteId"]!;

        var clientKey = "global";
        if (!string.IsNullOrWhiteSpace(config.KeyHeader) &&
            context.Request.Headers.TryGetValue(config.KeyHeader, out var headerVal) &&
            !string.IsNullOrWhiteSpace(headerVal.ToString()))
        {
            clientKey = headerVal.ToString();
        }

        // The limiter is shared across all routes (and its store may be shared across replicas),
        // so the counter key must carry the route id — otherwise two routes with the same
        // window/max and the same caller would consume each other's quota.
        var decision = _limiter.TryAcquire($"{routeId}\0{clientKey}", config.WindowSeconds, config.MaxRequests);
        if (!decision.Allowed)
        {
            // The algorithm computes its own retry hint: a fixed window rolls over on a boundary,
            // a sliding log frees a permit when its oldest request ages out. Only it knows.
            context.Response.Headers["Retry-After"] = decision.RetryAfterSeconds.ToString();
            context.Response.StatusCode = 429;
            await context.Response.WriteAsync("Rate limit exceeded. Try again later.");
            return;
        }

        await next(context);
    }

    public void ValidateConfig(JsonElement config)
    {
        var c = RateLimitConfig.From(config);
        if (c.WindowSeconds <= 0)
            throw new InvalidOperationException($"windowSeconds must be greater than zero (was {c.WindowSeconds}).");
        if (c.MaxRequests <= 0)
            throw new InvalidOperationException($"maxRequests must be greater than zero (was {c.MaxRequests}).");
    }

}

/// <summary>
/// Configuration for the <c>rate-limit</c> plugin.
/// Enforces a fixed-window request quota. Counters reset at the start of each window.
/// By default the quota is per-route (shared across all callers). Set <c>keyHeader</c>
/// to enforce the quota per caller using a header value such as an API key or client ID.
/// Place inside the route's <c>"config"</c> block.
/// </summary>
/// <example>
/// <code>
/// {
///   "name": "rate-limit",
///   "order": 2,
///   "enabled": true,
///   "config": {
///     "windowSeconds": 60,
///     "maxRequests":   100,
///     "keyHeader":     "X-Api-Key"
///   }
/// }
/// </code>
/// </example>
public sealed record RateLimitConfig
{
    /// <summary>Length of the fixed window in seconds. Default: <c>60</c>.</summary>
    [JsonPropertyName("windowSeconds")] public int     WindowSeconds { get; init; } = 60;

    /// <summary>Maximum requests allowed per window. Default: <c>100</c>.</summary>
    [JsonPropertyName("maxRequests")]   public long    MaxRequests   { get; init; } = 100;

    /// <summary>
    /// Header whose value is used as the rate-limit key, enabling per-caller quotas.
    /// Omit to apply a single shared quota across all callers on this route.
    /// Example: <c>"X-Api-Key"</c>, <c>"X-Client-Id"</c>.
    /// </summary>
    [JsonPropertyName("keyHeader")]     public string? KeyHeader     { get; init; }

    internal static RateLimitConfig From(JsonElement raw) =>
        raw.Deserialize<RateLimitConfig>(JsonOptions)
        ?? throw new InvalidOperationException("rate-limit plugin config is null or invalid.");

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

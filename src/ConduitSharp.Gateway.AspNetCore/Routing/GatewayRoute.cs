using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Routing;
using Yarp.ReverseProxy.Configuration;

namespace ConduitSharp.Gateway.Routing;

// ---------------------------------------------------------------------------
// Root
// ---------------------------------------------------------------------------

/// <summary>
/// Root wrapper that deserializes the top-level "routes" array from routes.json.
/// Call <see cref="Validate"/> immediately after deserialization to catch configuration errors
/// before the gateway starts serving traffic.
/// </summary>
public sealed class GatewayRoutesConfiguration
{
    private volatile List<GatewayRoute> _routes = [];

    /// <summary>The ordered list of route entries loaded from routes.json.</summary>
    [JsonPropertyName("routes")]
    public List<GatewayRoute> Routes { get => _routes; init => _routes = value; }

    /// <summary>
    /// Swaps the route list atomically. Admin hot reload uses this instead of mutating the list
    /// in place: this instance is a DI singleton read concurrently (readiness probe, Swagger), and
    /// a Clear()/AddRange() would give those readers a torn view. A reference swap cannot; anyone
    /// mid-enumeration keeps the snapshot they started with.
    /// </summary>
    internal void ReplaceRoutes(List<GatewayRoute> routes) => _routes = routes;

    /// <summary>
    /// How routes.json is read. Case-insensitive so YARP's own config records — which carry no
    /// <c>[JsonPropertyName]</c> attributes and would otherwise demand PascalCase — can be written
    /// in the same camelCase as everything else. Enums are strings, not the numeric default.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            // Order matters, and subtly: a converter in this collection beats a [JsonConverter]
            // attribute on the type. Registering JsonStringEnumConverter alone would therefore
            // shadow PluginName's StrictEnumConverter and break kebab-case ("jwt-auth"), so the
            // strict converters go first and the general one only catches what is left — which is
            // what YARP's enums (HeaderMatchMode, QueryParameterMatchMode, …) need.
            new StrictEnumConverter<PluginName>(),
            new JsonStringEnumConverter(),
        },
    };

    /// <summary>Reads a routes.json document. Does not validate — call <see cref="Validate"/>.</summary>
    public static GatewayRoutesConfiguration Parse(string json) =>
        JsonSerializer.Deserialize<GatewayRoutesConfiguration>(json, JsonOptions)
        ?? throw new InvalidOperationException("routes.json deserialized to null.");

    /// <summary>
    /// Validates everything the gateway owns. YARP validates its own half (route match, cluster
    /// destinations, policy names) when the config is loaded, so this covers the rest: route ids,
    /// plugin declarations, and the retry / circuit-breaker blocks.
    ///
    /// Route IDs become filesystem directory names under the plugins root (created and deleted
    /// recursively), so path separators or dots in an ID would be a traversal primitive — hence
    /// the strict character set.
    /// </summary>
    public void Validate()
    {
        ValidateRouteIds();
        foreach (var route in Routes)
        {
            ValidatePluginVariants(route);
            ValidateCluster(route);
            ValidateRetry(route);
            ValidateCircuitBreaker(route);
        }
    }

    private void ValidateRouteIds()
    {
        var invalid = Routes
            .Select(r => r.Id)
            .Where(id => string.IsNullOrWhiteSpace(id) ||
                         !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            .ToList();

        if (invalid.Count > 0)
            throw new InvalidOperationException(
                "Route IDs must be non-empty and contain only letters, digits, hyphens, " +
                $"and underscores. Invalid: {string.Join(", ", invalid.Select(i => $"'{i}'"))}");

        var duplicates = Routes
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate route IDs found in configuration: {string.Join(", ", duplicates)}");
    }

    // A variant disambiguates Custom plugins; it is meaningless (and a likely mistake)
    // on a built-in plugin name, and required on Custom so a route resolves unambiguously.
    private static void ValidatePluginVariants(GatewayRoute route)
    {
        foreach (var plugin in route.Plugins)
        {
            var hasVariant = !string.IsNullOrWhiteSpace(plugin.Variant);
            if (plugin.Name == PluginName.Custom && !hasVariant)
                throw new InvalidOperationException(
                    $"Route '{route.Id}': a 'custom' plugin requires a 'variant' identifying which custom plugin to run.");
            if (plugin.Name != PluginName.Custom && hasVariant)
                throw new InvalidOperationException(
                    $"Route '{route.Id}': plugin '{plugin.Name}' must not specify a 'variant' (only 'custom' plugins use variants).");
        }
    }

    // A route that forwards must name at least one destination targeting http(s). YARP checks the
    // address parses; it does not care about the scheme, and a gateway does.
    private static void ValidateCluster(GatewayRoute route)
    {
        if (route.Cluster is not { } cluster) return;

        if (cluster.Destinations is not { Count: > 0 } destinations)
            throw new InvalidOperationException(
                $"Route '{route.Id}': cluster is configured but has no destinations.");

        foreach (var (id, destination) in destinations)
        {
            if (!Uri.TryCreate(destination.Address, UriKind.Absolute, out var address))
                throw new InvalidOperationException(
                    $"Route '{route.Id}': destination '{id}' address '{destination.Address}' is not an absolute URL.");

            if (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException(
                    $"Route '{route.Id}': destination '{id}' address '{destination.Address}' must use http or https.");
        }
    }

    private static void ValidateRetry(GatewayRoute route)
    {
        if (route.Retry is not { } retry) return;

        if (retry.MaxAttempts > 1 && route.StreamOnly)
            throw new InvalidOperationException(
                $"Route '{route.Id}': cannot configure both retry (maxAttempts > 1) and streamOnly. Retries require a buffered, seekable stream.");

        if (retry.MaxAttempts < 1)
            throw new InvalidOperationException(
                $"Route '{route.Id}': retry.maxAttempts must be at least 1 (was {retry.MaxAttempts}).");
        if (retry.DelayMs < 0)
            throw new InvalidOperationException(
                $"Route '{route.Id}': retry.delayMs cannot be negative (was {retry.DelayMs}).");
        if (retry.RetryOn.Count == 0 || retry.RetryOn.Any(code => code is < 100 or > 599))
            throw new InvalidOperationException(
                $"Route '{route.Id}': retry.retryOn must list HTTP status codes between 100 and 599.");
    }

    private static void ValidateCircuitBreaker(GatewayRoute route)
    {
        if (route.CircuitBreaker is not { } breaker) return;

        if (breaker.CooldownMs <= 0)
            throw new InvalidOperationException(
                $"Route '{route.Id}': circuitBreaker.cooldownMs must be greater than zero (was {breaker.CooldownMs}).");
    }
}

// ---------------------------------------------------------------------------
// Route
// ---------------------------------------------------------------------------

/// <summary>
/// One route entry: what to match, where to forward it, and which ordered plugins to apply.
///
/// The two halves are explicit. <see cref="Route"/> and <see cref="Cluster"/> are YARP's own config
/// records, used verbatim — so every YARP feature (session affinity, active health checks,
/// transforms, host matching, TLS tuning) is configurable the day YARP ships it, with no schema
/// work here and no projection layer to drift out of sync. Everything else on this type is what
/// YARP has no concept of: retries, the circuit breaker, the plugin chain.
/// </summary>
public sealed class GatewayRoute
{
    /// <summary>
    /// Unique identifier. Also becomes YARP's <c>RouteId</c> and <c>ClusterId</c> — routes and
    /// clusters are 1:1 here — so it never has to be repeated inside <see cref="Route"/> or
    /// <see cref="Cluster"/>.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Human-readable description of this route's purpose. Not used at runtime.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// YARP's route config: <c>match</c> (path, methods, headers, query parameters, hosts) plus
    /// anything else <c>RouteConfig</c> exposes. <c>RouteId</c>, <c>ClusterId</c> and <c>Order</c>
    /// are filled in from <see cref="Id"/> and the route's position in routes.json — declaration
    /// order breaks overlaps, so the first route declared wins.
    /// </summary>
    [JsonPropertyName("route")]
    public required RouteConfig Route { get; init; }

    /// <summary>
    /// YARP's cluster config: destinations, load-balancing policy, timeouts, TLS, health checks.
    /// Null when the route is handled entirely by plugins (a short-circuit response, no upstream).
    /// <c>ClusterId</c> is filled in from <see cref="Id"/>; passive health checking is wired up
    /// from <see cref="CircuitBreaker"/>.
    /// </summary>
    [JsonPropertyName("cluster")]
    public ClusterConfig? Cluster { get; init; }

    /// <summary>
    /// Retry policy for transient upstream failures. YARP ships no retry — a proxy cannot safely
    /// replay a half-streamed body — so the gateway buffers the request and drives attempts itself.
    /// Null means no retries.
    /// </summary>
    [JsonPropertyName("retry")]
    public RetryConfig? Retry { get; init; }

    /// <summary>
    /// Per-node circuit breaker. Null means no circuit breaking (every destination always eligible).
    /// </summary>
    [JsonPropertyName("circuitBreaker")]
    public CircuitBreakerConfig? CircuitBreaker { get; init; }

    /// <summary>
    /// Ordered list of plugins to execute for this route, by <see cref="PluginConfig.Order"/>.
    /// </summary>
    [JsonPropertyName("plugins")]
    public List<PluginConfig> Plugins { get; init; } = [];

    /// <summary>
    /// Optional OpenAPI spec source. When set, this route's spec appears in the gateway's
    /// aggregated Swagger UI at /swagger.
    /// </summary>
    [JsonPropertyName("swagger")]
    public SwaggerOptions? Swagger { get; init; }

    /// <summary>
    /// Maximum request body bytes buffered for this route, overriding the global
    /// <c>Gateway:RequestLimits:MaxRequestBodyBytes</c>. Bodies over the limit are rejected with
    /// 413. Null inherits the global limit; zero or negative disables the per-request check for
    /// this route (the global total-buffering budget still applies).
    /// </summary>
    [JsonPropertyName("maxRequestBodyBytes")]
    public long? MaxRequestBodyBytes { get; init; }

    /// <summary>
    /// If true, bypasses eagerly buffering the request body. Plugins will not be able to rewind the stream.
    /// Incompatible with <see cref="Retry"/>. Default is <c>false</c>.
    /// </summary>
    [JsonPropertyName("streamOnly")]
    public bool StreamOnly { get; init; } = false;
}

// ---------------------------------------------------------------------------
// Reliability — the things YARP has no concept of
// ---------------------------------------------------------------------------

/// <summary>
/// Retry policy for transient upstream failures, applied around the forwarder for idempotent
/// methods only (<c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c>, <c>PUT</c>, <c>DELETE</c>, <c>TRACE</c>)
/// — a <c>POST</c>/<c>PATCH</c> may already have been applied upstream. Attempts re-run load
/// balancing, so a retry lands on a different node.
/// </summary>
/// <example>
/// <code>
/// "retry": {
///   "maxAttempts": 3,
///   "delayMs":     200,
///   "backoff":     "Exponential",
///   "jitter":      true,
///   "retryOn":     [502, 503, 504]
/// }
/// </code>
/// </example>
public sealed class RetryConfig
{
    /// <summary>Total attempts including the first. <c>1</c> disables retries. Default: <c>1</c>.</summary>
    [JsonPropertyName("maxAttempts")]
    public int MaxAttempts { get; init; } = 1;

    /// <summary>Base delay between attempts in milliseconds. Default: <c>0</c>.</summary>
    [JsonPropertyName("delayMs")]
    public int DelayMs { get; init; } = 0;

    /// <summary>
    /// How the delay grows across attempts: "Fixed", "Linear", or "Exponential". Default: <c>Fixed</c>.
    /// </summary>
    [JsonPropertyName("backoff")]
    public RetryBackoff Backoff { get; init; } = RetryBackoff.Fixed;

    /// <summary>Adds randomized jitter to each delay to avoid retry stampedes. Default: <c>false</c>.</summary>
    [JsonPropertyName("jitter")]
    public bool Jitter { get; init; } = false;

    /// <summary>
    /// Upstream status codes that trigger a retry. Default: <c>[502, 503, 504]</c>. A connection
    /// failure or timeout always retries, whatever this says.
    /// </summary>
    [JsonPropertyName("retryOn")]
    public IReadOnlyList<int> RetryOn { get; init; } = [502, 503, 504];
}

/// <summary>
/// Per-node circuit breaker. After <see cref="Threshold"/> consecutive failures a destination is
/// taken out of the load balancer's rotation for <see cref="CooldownMs"/>, after which one trial
/// request decides whether it recovers or opens again. A client disconnecting mid-request is never
/// counted as a node failure.
/// </summary>
/// <example>
/// <code>
/// "circuitBreaker": { "threshold": 5, "cooldownMs": 10000 }
/// </code>
/// </example>
public sealed class CircuitBreakerConfig
{
    /// <summary>Consecutive failures against one node before its circuit opens. Default: <c>5</c>.</summary>
    [JsonPropertyName("threshold")]
    public int Threshold { get; init; } = 5;

    /// <summary>How long an open circuit stays open, in milliseconds. Default: <c>10000</c> (10 s).</summary>
    [JsonPropertyName("cooldownMs")]
    public int CooldownMs { get; init; } = 10_000;
}

/// <summary>Delay growth between upstream retry attempts.</summary>
public enum RetryBackoff
{
    /// <summary>Every attempt waits the same <c>delayMs</c>.</summary>
    Fixed,

    /// <summary>The delay grows by <c>delayMs</c> each attempt.</summary>
    Linear,

    /// <summary>The delay doubles each attempt.</summary>
    Exponential,
}

/// <summary>
/// The built-in YARP load-balancing policies. Each member's name <em>is</em> the policy name — YARP
/// declares them as <c>nameof(...)</c> — so <c>ToString()</c> is what goes in
/// <c>cluster.loadBalancingPolicy</c>. Use it when building routes in C# so a typo is a compile
/// error; the JSON stays a free string so a drop-in <c>ILoadBalancingPolicy</c> is nameable too.
/// </summary>
public enum LoadBalancingPolicy
{
    /// <summary>Cycle through destinations in order. The gateway's default.</summary>
    RoundRobin,

    /// <summary>Pick a destination at random.</summary>
    Random,

    /// <summary>Pick two at random and take the less busy — Random's speed without its worst case.</summary>
    PowerOfTwoChoices,

    /// <summary>Fewest in-flight requests. Examines every destination.</summary>
    LeastRequests,

    /// <summary>Always the alphabetically first healthy destination — dual-node failover.</summary>
    FirstAlphabetical,
}

// ---------------------------------------------------------------------------
// Swagger aggregation
// ---------------------------------------------------------------------------

/// <summary>
/// Optional OpenAPI spec source for this route.
/// Exactly one of <see cref="FetchFrom"/> or <see cref="SpecFile"/> should be set.
/// </summary>
public sealed class SwaggerOptions
{
    /// <summary>
    /// URL of the upstream's swagger.json endpoint, fetched live on each request.
    /// Example: "http://user-service:8080/swagger/v1/swagger.json"
    /// </summary>
    [JsonPropertyName("fetchFrom")]
    public string? FetchFrom { get; init; }

    /// <summary>
    /// Path to a local OpenAPI JSON file, resolved relative to the gateway working directory.
    /// Example: "./specs/user-service.json"
    /// </summary>
    [JsonPropertyName("specFile")]
    public string? SpecFile { get; init; }
}

// ---------------------------------------------------------------------------
// Plugins
// ---------------------------------------------------------------------------

/// <summary>
/// Declares one plugin in a route's pipeline. <see cref="Name"/> is matched against registered
/// <c>IPipelinePlugin</c> implementations; <see cref="Config"/> is a free-form JSON object passed
/// to the plugin.
/// </summary>
public sealed class PluginConfig
{
    /// <summary>
    /// Plugin identifier — strictly validated at JSON load time. JSON accepts kebab-case:
    /// "jwt-auth", "api-key-auth", "rate-limit", "header-transform", "cache".
    /// An unrecognised value throws <see cref="JsonException"/>.
    /// </summary>
    [JsonPropertyName("name")]
    public required PluginName Name { get; init; }

    /// <summary>
    /// Selects among plugins sharing <see cref="PluginName.Custom"/>. Required when
    /// <see cref="Name"/> is <see cref="PluginName.Custom"/>; must be omitted otherwise.
    /// </summary>
    [JsonPropertyName("variant")]
    public string? Variant { get; init; }

    /// <summary>When false the plugin is skipped entirely without error.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Ascending execution order within the route's plugin chain. Lower numbers run first
    /// (e.g. auth at 1, transform at 3).
    /// </summary>
    [JsonPropertyName("order")]
    public int Order { get; init; }

    /// <summary>
    /// Plugin-specific settings, kept as a raw <see cref="JsonElement"/> so each plugin
    /// deserializes only what it needs without coupling this schema to plugin internals.
    /// </summary>
    [JsonPropertyName("config")]
    public JsonElement Config { get; init; }
}

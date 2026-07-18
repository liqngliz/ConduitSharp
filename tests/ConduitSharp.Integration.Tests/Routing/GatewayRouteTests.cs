using System.Text.Json;
using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway.Routing;
using Xunit;

namespace ConduitSharp.Integration.Tests.Routing;

/// <summary>
/// The routes.json schema: how it deserializes, and what the gateway validates about it.
///
/// The route's <c>route</c> and <c>cluster</c> blocks are YARP's own config records, so YARP owns
/// validating them (match syntax, destination addresses, policy names) at config load. What is
/// tested here is the half ConduitSharp owns: route ids, the plugin chain, retry, and the circuit
/// breaker — plus the parsing rules that let the whole document be written in one style.
/// </summary>
public class GatewayRouteTests
{
    // Inline copy of routes.json so the test has no dependency on filesystem paths.
    private const string RoutesJson = """
        {
          "routes": [
            {
              "id": "user-service-route",
              "description": "Public user profile endpoint",
              "route": {
                "match": {
                  "path": "/api/users/{**catch-all}",
                  "methods": [ "GET", "POST" ]
                }
              },
              "cluster": {
                "loadBalancingPolicy": "RoundRobin",
                "destinations": {
                  "node-0": { "address": "http://user-service-1:8080" },
                  "node-1": { "address": "http://user-service-2:8080" }
                },
                "httpRequest": { "activityTimeout": "00:00:05" }
              },
              "retry": { "maxAttempts": 2 },
              "circuitBreaker": { "threshold": 5, "cooldownMs": 10000 },
              "plugins": [
                { "name": "jwt-auth",   "enabled": true, "order": 1, "config": {} },
                { "name": "rate-limit", "enabled": true, "order": 2, "config": {} }
              ]
            },
            {
              "id": "admin-route",
              "route": {
                "match": {
                  "path": "/admin/{**rest}",
                  "headers": [ { "name": "X-Internal", "values": [ "yes" ], "mode": "ExactHeader" } ]
                }
              },
              "cluster": {
                "loadBalancingPolicy": "Random",
                "destinations": { "node-0": { "address": "http://admin-service:9090" } }
              },
              "plugins": []
            },
            {
              "id": "script-route",
              "route": { "match": { "path": "/reports/margin" } },
              "cluster": null,
              "plugins": [
                { "name": "custom", "variant": "power-shell", "order": 1, "config": {} }
              ]
            }
          ]
        }
        """;

    private static GatewayRoutesConfiguration Parse() => GatewayRoutesConfiguration.Parse(RoutesJson);

    // -----------------------------------------------------------------------
    // Deserialization — YARP's records bind from the same camelCase as ours
    // -----------------------------------------------------------------------

    [Fact]
    public void Deserialize_LoadsEveryRoute()
    {
        Assert.Equal(3, Parse().Routes.Count);
    }

    [Fact]
    public void RouteBlock_IsYarpsRouteConfig_AndBindsFromCamelCase()
    {
        var route = Parse().Routes[0];

        Assert.Equal("user-service-route", route.Id);
        Assert.Equal("/api/users/{**catch-all}", route.Route.Match.Path);
        Assert.Equal(["GET", "POST"], route.Route.Match.Methods);
    }

    [Fact]
    public void ClusterBlock_IsYarpsClusterConfig_WithNamedDestinations()
    {
        var cluster = Parse().Routes[0].Cluster;

        Assert.NotNull(cluster);
        Assert.Equal("RoundRobin", cluster.LoadBalancingPolicy);
        Assert.Equal(2, cluster.Destinations!.Count);
        Assert.Equal("http://user-service-1:8080", cluster.Destinations["node-0"].Address);
        Assert.Equal(TimeSpan.FromSeconds(5), cluster.HttpRequest!.ActivityTimeout);
    }

    [Fact]
    public void HeaderMatch_BindsYarpsMatcherObjects_IncludingTheStringEnumMode()
    {
        // The whole reason for taking YARP's types: header matching gains modes (Prefix, Contains,
        // NotExists, …) the old dictionary shape could not express.
        var header = Assert.Single(Parse().Routes[1].Route.Match.Headers!);

        Assert.Equal("X-Internal", header.Name);
        Assert.Equal(["yes"], header.Values);
        Assert.Equal(Yarp.ReverseProxy.Configuration.HeaderMatchMode.ExactHeader, header.Mode);
    }

    [Fact]
    public void NullCluster_MeansAPluginOnlyRoute()
    {
        Assert.Null(Parse().Routes[2].Cluster);
    }

    [Fact]
    public void PluginNames_DeserializeFromKebabCase()
    {
        var plugins = Parse().Routes[0].Plugins;

        Assert.Equal(PluginName.JwtAuth,   plugins[0].Name);
        Assert.Equal(PluginName.RateLimit, plugins[1].Name);
        Assert.Equal([1, 2], plugins.Select(p => p.Order));
        Assert.All(plugins, p => Assert.True(p.Enabled));
    }

    [Fact]
    public void InvalidPluginName_ThrowsJsonException()
    {
        // Regression: registering JsonStringEnumConverter in the shared options would shadow
        // PluginName's StrictEnumConverter (options converters beat type attributes), quietly
        // breaking kebab-case and this error.
        var json = """{ "routes": [{ "id": "x", "route": { "match": { "path": "/" } }, "plugins": [{ "name": "no-such-plugin", "order": 1 }] }] }""";

        Assert.Throws<JsonException>(() => GatewayRoutesConfiguration.Parse(json));
    }

    // -----------------------------------------------------------------------
    // Reliability blocks — ours, not YARP's
    // -----------------------------------------------------------------------

    [Fact]
    public void RetryBlock_Deserializes_AllFields()
    {
        var json = """
            { "routes": [{ "id": "r", "route": { "match": { "path": "/a" } },
              "cluster": { "destinations": { "d": { "address": "http://svc:8080" } } },
              "retry": { "maxAttempts": 3, "delayMs": 200, "backoff": "Exponential", "jitter": true, "retryOn": [500, 502] },
              "plugins": [] }] }
            """;

        var retry = GatewayRoutesConfiguration.Parse(json).Routes[0].Retry;

        Assert.NotNull(retry);
        Assert.Equal(3, retry.MaxAttempts);
        Assert.Equal(200, retry.DelayMs);
        Assert.Equal(RetryBackoff.Exponential, retry.Backoff);
        Assert.True(retry.Jitter);
        Assert.Equal([500, 502], retry.RetryOn);
    }

    [Fact]
    public void RetryBlock_DefaultsToTheStandardRetryableStatuses()
    {
        Assert.Equal([502, 503, 504], Parse().Routes[0].Retry!.RetryOn);
    }

    [Fact]
    public void CircuitBreakerBlock_Deserializes()
    {
        var breaker = Parse().Routes[0].CircuitBreaker;

        Assert.NotNull(breaker);
        Assert.Equal(5, breaker.Threshold);
        Assert.Equal(10_000, breaker.CooldownMs);
    }

    [Fact]
    public void NoRetryOrCircuitBreaker_IsNull_NotADefaultedObject()
    {
        // Absent means off — the gateway must not invent a retry policy nobody asked for.
        var route = Parse().Routes[1];

        Assert.Null(route.Retry);
        Assert.Null(route.CircuitBreaker);
    }

    // -----------------------------------------------------------------------
    // Validate — route ids
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_PassesForAWellFormedDocument() => Parse().Validate();

    [Fact]
    public void Validate_ThrowsWhenDuplicateIdExists()
    {
        var json = """
            { "routes": [
              { "id": "dup", "route": { "match": { "path": "/a" } }, "plugins": [] },
              { "id": "dup", "route": { "match": { "path": "/b" } }, "plugins": [] }
            ] }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => GatewayRoutesConfiguration.Parse(json).Validate());
        Assert.Contains("dup", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_DuplicateIdCheck_IsCaseInsensitive()
    {
        var json = """
            { "routes": [
              { "id": "My-Route", "route": { "match": { "path": "/a" } }, "plugins": [] },
              { "id": "my-route", "route": { "match": { "path": "/b" } }, "plugins": [] }
            ] }
            """;

        Assert.Throws<InvalidOperationException>(() => GatewayRoutesConfiguration.Parse(json).Validate());
    }

    [Theory]
    [InlineData("../evil")]   // route ids become directory names under the plugins root
    [InlineData("a/b")]
    [InlineData("with.dot")]
    [InlineData("")]
    public void Validate_RejectsIdsThatCouldEscapeThePluginsRoot(string id)
    {
        var json = $$"""
            { "routes": [{ "id": "{{id}}", "route": { "match": { "path": "/a" } }, "plugins": [] }] }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => GatewayRoutesConfiguration.Parse(json).Validate());
        Assert.Contains("Route IDs", ex.Message, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Validate — plugins
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_ThrowsWhenCustomPluginHasNoVariant()
    {
        var json = """
            { "routes": [{ "id": "r", "route": { "match": { "path": "/a" } },
              "plugins": [{ "name": "custom", "order": 1 }] }] }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => GatewayRoutesConfiguration.Parse(json).Validate());
        Assert.Contains("variant", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsWhenBuiltInPluginHasVariant()
    {
        var json = """
            { "routes": [{ "id": "r", "route": { "match": { "path": "/a" } },
              "plugins": [{ "name": "cache", "variant": "nope", "order": 1 }] }] }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => GatewayRoutesConfiguration.Parse(json).Validate());
        Assert.Contains("variant", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PassesWhenCustomPluginHasVariant() => Parse().Validate(); // script-route

    // -----------------------------------------------------------------------
    // Validate — cluster destinations (the gateway cares about scheme; YARP does not)
    // -----------------------------------------------------------------------

    private static string ClusterWith(string destinations) => $$"""
        { "routes": [{ "id": "r", "route": { "match": { "path": "/a" } },
          "cluster": { "destinations": {{destinations}} }, "plugins": [] }] }
        """;

    [Fact]
    public void Validate_ThrowsWhenClusterHasNoDestinations()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GatewayRoutesConfiguration.Parse(ClusterWith("{}")).Validate());
        Assert.Contains("destinations", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsWhenDestinationIsNotHttp()
    {
        // ftp:// is a valid absolute Uri — so it deserializes — but not a valid gateway upstream.
        var ex = Assert.Throws<InvalidOperationException>(
            () => GatewayRoutesConfiguration.Parse(ClusterWith("""{ "d": { "address": "ftp://svc:21" } }""")).Validate());
        Assert.Contains("http", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsWhenDestinationIsARelativePath()
    {
        // Careful: .NET parses a leading-slash path as an absolute file:// URI on Unix, so this
        // gets caught by the scheme check rather than the absolute-URI check. Either way it must
        // not reach the forwarder.
        var ex = Assert.Throws<InvalidOperationException>(
            () => GatewayRoutesConfiguration.Parse(ClusterWith("""{ "d": { "address": "/relative" } }""")).Validate());
        Assert.Contains("http", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsWhenDestinationIsNotAUrlAtAll()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GatewayRoutesConfiguration.Parse(ClusterWith("""{ "d": { "address": "http://[malformed" } }""")).Validate());
        Assert.Contains("absolute", ex.Message, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Validate — retry / circuit breaker
    // -----------------------------------------------------------------------

    private static string RouteWith(string block) => $$"""
        { "routes": [{ "id": "r", "route": { "match": { "path": "/a" } },
          "cluster": { "destinations": { "d": { "address": "http://svc:8080" } } },
          {{block}}, "plugins": [] }] }
        """;

    [Fact]
    public void Validate_ThrowsWhenMaxAttemptsIsBelowOne()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GatewayRoutesConfiguration.Parse(RouteWith("""  "retry": { "maxAttempts": 0 }  """)).Validate());
        Assert.Contains("maxAttempts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsWhenRetryOnIsNotAnHttpStatus()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GatewayRoutesConfiguration.Parse(RouteWith("""  "retry": { "maxAttempts": 2, "retryOn": [42] }  """)).Validate());
        Assert.Contains("retryOn", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsWhenCircuitBreakerCooldownIsNotPositive()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GatewayRoutesConfiguration.Parse(RouteWith("""  "circuitBreaker": { "threshold": 3, "cooldownMs": 0 }  """)).Validate());
        Assert.Contains("cooldownMs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidBackoff_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(
            () => GatewayRoutesConfiguration.Parse(RouteWith("""  "retry": { "maxAttempts": 2, "backoff": "Quadratic" }  """)));
    }
}

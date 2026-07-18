using System.Text.Json;
using Xunit;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Traffic.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Traffic.Tests;

public sealed class RateLimitPluginTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// One real RateLimitPlugin plus the serialized config each context gets stamped
    /// with before execution — the same JSON round-trip (and real RateLimitConfig.From
    /// parse) a routes.json config block goes through in production.
    /// </summary>
    private sealed class ConfiguredPlugin(RateLimitConfig config)
    {
        private readonly RateLimitPlugin _plugin = new();
        private readonly JsonElement _configJson = JsonSerializer.SerializeToElement(config);

        public Task ExecuteAsync(HttpContext context, RequestDelegate next)
        {
            
            return _plugin.ExecuteAsync(context, _configJson, next);
        }
    }

    private static ConfiguredPlugin Plugin(
        int window     = 60,
        int max        = 100,
        string? header = null) =>
        new(new RateLimitConfig
        {
            WindowSeconds = window,
            MaxRequests   = max,
            KeyHeader     = header
        });

    private static HttpContext Context(string routeId = "r1", Dictionary<string, string>? headers = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/test";
        if (headers != null)
        {
            foreach (var h in headers) ctx.Request.Headers[h.Key] = h.Value;
        }
        ctx.Items["ConduitSharp.RouteId"] = routeId;
        return ctx;
    }

    private static (RequestDelegate, Func<bool>) TrackingNext()
    {
        var called = false;
        return (_ => { called = true; return Task.CompletedTask; }, () => called);
    }

    private static RequestDelegate NoOpNext() => _ => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Under limit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_UnderLimit_CallsNext()
    {
        var plugin            = Plugin(max: 10);
        var context           = Context();
        var (next, wasCalled) = TrackingNext();

        await plugin.ExecuteAsync(context, next);

        
        Assert.True(wasCalled());
    }

    // -------------------------------------------------------------------------
    // Over limit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OverLimit_ShortCircuits429()
    {
        var plugin = Plugin(window: 60, max: 1);

        await plugin.ExecuteAsync(Context(), NoOpNext()); // consume the one permit

        var blocked = Context();
        await plugin.ExecuteAsync(blocked, NoOpNext());

        
        Assert.Equal(429, blocked.Response.StatusCode);
        // Assert.Contains("Rate limit exceeded", blocked.ShortCircuitBody);
    }

    [Fact]
    public async Task ExecuteAsync_OverLimit_RetryAfterIsTimeRemainingInWindow()
    {
        var plugin = Plugin(window: 30, max: 1);
        await plugin.ExecuteAsync(Context(), NoOpNext());

        var blocked = Context();
        await plugin.ExecuteAsync(blocked, NoOpNext());

        // Not the full window length — the seconds left until this fixed window rolls over.
        var retryAfter = int.Parse(blocked.Response.Headers["Retry-After"]!);
        Assert.InRange(retryAfter, 1, 30);
    }

    // -------------------------------------------------------------------------
    // Global key (no KeyHeader configured)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoKeyHeader_AllRequestsShareGlobalCounter()
    {
        var plugin = Plugin(max: 1); // no keyHeader — all requests use "global"
        await plugin.ExecuteAsync(Context(), NoOpNext());

        var second = Context();
        await plugin.ExecuteAsync(second, NoOpNext());

        Assert.Equal(429, second.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Per-client key via header
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PerClientKey_DifferentClientsHaveIndependentLimits()
    {
        var plugin = Plugin(max: 1, header: "X-Client-Id");

        var clientA = Context(headers: new() { ["X-Client-Id"] = "client-a" });
        var clientB = Context(headers: new() { ["X-Client-Id"] = "client-b" });
        var (nextA, calledA) = TrackingNext();
        var (nextB, calledB) = TrackingNext();

        await plugin.ExecuteAsync(clientA, nextA);
        await plugin.ExecuteAsync(clientB, nextB); // different client — own quota

        Assert.True(calledA());
        Assert.True(calledB());
    }

    [Fact]
    public async Task ExecuteAsync_KeyHeaderAbsent_FallsBackToGlobalKey()
    {
        var plugin = Plugin(max: 1, header: "X-Client-Id");
        // No X-Client-Id header — falls back to "global"
        await plugin.ExecuteAsync(Context(), NoOpNext());

        var second = Context(); // also no header — same "global" slot
        await plugin.ExecuteAsync(second, NoOpNext());

        Assert.Equal(429, second.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_KeyHeaderPresentButEmpty_FallsBackToGlobalKey()
    {
        var plugin = Plugin(max: 1, header: "X-Client-Id");
        await plugin.ExecuteAsync(
            Context(headers: new() { ["X-Client-Id"] = "" }),
            NoOpNext());

        var second = Context(headers: new() { ["X-Client-Id"] = "   " });
        await plugin.ExecuteAsync(second, NoOpNext());

        Assert.Equal(429, second.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Different routes have independent limiters
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_DifferentRoutes_HaveIndependentLimiters()
    {
        var plugin = Plugin(max: 1);
        await plugin.ExecuteAsync(Context(routeId: "route-a"), NoOpNext());

        var routeB = Context(routeId: "route-b");
        var (next, wasCalled) = TrackingNext();
        await plugin.ExecuteAsync(routeB, next);

        
        Assert.True(wasCalled());
    }

    private sealed class SingleServiceProvider(IRateLimitStore store) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IRateLimitStore) ? store : null;
    }

    [Fact]
    public async Task ExecuteAsync_SharedStore_DifferentRoutesSameConfig_DoNotShareCounters()
    {
        // The DI path hands every limiter the same IRateLimitStore singleton. The store
        // key must therefore carry the route id — without it, two routes with identical
        // window/max and the same caller consumed one shared quota.
        var config = JsonSerializer.SerializeToElement(
            new RateLimitConfig { WindowSeconds = 60, MaxRequests = 1 });
        var plugin = new RateLimitPlugin(new SingleServiceProvider(new InMemoryRateLimitStore()));

        var routeA = Context(routeId: "route-a");
        await plugin.ExecuteAsync(routeA, config, NoOpNext()); // consumes route-a's single permit

        var routeB = Context(routeId: "route-b");
        var (next, wasCalled) = TrackingNext();
        await plugin.ExecuteAsync(routeB, config, next);

         // own quota, untouched by route-a
        Assert.True(wasCalled());
    }

    // -------------------------------------------------------------------------
    // ValidateConfig — rejects non-positive limits at startup
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("""{ "windowSeconds": 0, "maxRequests": 100 }""")]
    [InlineData("""{ "windowSeconds": 60, "maxRequests": 0 }""")]
    [InlineData("""{ "windowSeconds": -5, "maxRequests": 100 }""")]
    public void ValidateConfig_NonPositiveLimits_Throws(string configJson)
    {
        var plugin = new RateLimitPlugin();
        var config = JsonDocument.Parse(configJson).RootElement;

        Assert.Throws<InvalidOperationException>(() => plugin.ValidateConfig(config));
    }

    [Fact]
    public void ValidateConfig_ValidLimits_DoesNotThrow()
    {
        var plugin = new RateLimitPlugin();
        var config = JsonDocument.Parse("""{ "windowSeconds": 60, "maxRequests": 100 }""").RootElement;

        plugin.ValidateConfig(config); // no throw
    }
}

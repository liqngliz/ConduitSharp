using System.Collections.Concurrent;
using System.Net;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using ConduitSharp.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Integration tests for the POST /admin/routes/reload endpoint.
/// Each test manages its own factory so the Gateway__AdminKeyHash env var
/// is isolated to the gateway startup for that test.
/// </summary>
public sealed class AdminApiTests : IAsyncDisposable
{
    private FakeUpstream? _upstream;

    private static readonly string TestKeyHash =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("test-key")));

    private static readonly string CorrectKeyHash =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("correct-key")));

    private async Task<FakeUpstream> Upstream()
        => _upstream ??= await FakeUpstream.StartAsync();

    public async ValueTask DisposeAsync()
    {
        if (_upstream is not null) await _upstream.DisposeAsync();
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", null);
    }

    [Fact]
    public async Task Reload_NoAdminKeyConfigured_TreatedAsNormalRequest_Returns404()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", null);

        await using var upstream = await FakeUpstream.StartAsync();

        var routes = $$"""
            {
              "routes": [{
                "id": "api-only",
                "route": { "match": { "path": "/api/{**rest}" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(upstream, routes);
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/admin/routes/reload",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reload_WrongAdminKey_Returns401()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", CorrectKeyHash);
        await using var factory = await GatewayFactory.CreateAsync(await Upstream());
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "wrong-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reload_InvalidJson_Returns400()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        await using var factory = await GatewayFactory.CreateAsync(await Upstream());
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent("NOT_VALID_JSON", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid routes configuration", body);
    }

    [Fact]
    public async Task Reload_InvalidJson_LogsError()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        var upstream = await Upstream();
        var logCollector = new CollectingLoggerProvider();

        await using var factory = await GatewayFactory.CreateAsync(
            upstream,
            configureWebHost: builder => builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(logCollector);
            }));
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent("NOT_VALID_JSON", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(logCollector.Entries, entry => entry.Message.Contains("Admin route reload rejected"));
    }

    [Fact]
    public async Task Reload_ValidJson_Returns200AndWritesFile()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        var upstream = await Upstream();
        await using var factory = await GatewayFactory.CreateAsync(upstream);
        var client = factory.CreateClient();

        var routesPath = Environment.GetEnvironmentVariable("Gateway__RoutesPath")!;
        var originalContent = await File.ReadAllTextAsync(routesPath);

        var newRoutes = $$"""
            {
              "routes": [{
                "id": "reloaded-route",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:09.999" }
                },
                "plugins": []
              }]
            }
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent(newRoutes, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Routes reloaded", body);

        var written = await File.ReadAllTextAsync(routesPath);
        Assert.Equal(newRoutes, written);
        Assert.NotEqual(originalContent, written);
    }

    [Fact]
    public async Task Reload_NewRouteTable_ServesNextRequest_WithoutRestart()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        var upstream = await Upstream();
        upstream.RespondWith(200, "upstream");

        var initial = $$"""
            {
              "routes": [{
                "id": "old-route",
                "route": { "match": { "path": "/old" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;
        await using var factory = await GatewayFactory.CreateAsync(upstream, initial);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK,       (await client.GetAsync("/old")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/new")).StatusCode);

        var reloaded = $$"""
            {
              "routes": [{
                "id": "new-route",
                "route": { "match": { "path": "/new" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent(reloaded, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "test-key");

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,       (await client.GetAsync("/new")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/old")).StatusCode);
    }

    [Fact]
    public async Task Reload_PluginOnlyRoute_IsHotSwappedToo()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        var upstream = await Upstream();

        await using var factory = await GatewayFactory.CreateAsync(upstream);
        var client = factory.CreateClient();

        var reloaded = """
            {
              "routes": [{
                "id": "plugin-only",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": null,
                "plugins": []
              }]
            }
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent(reloaded, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "test-key");

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        var response = await client.GetAsync("/anything");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task Reload_UnknownLoadBalancingStrategy_Returns400_AndKeepsServingOldRoutes()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        var upstream = await Upstream();
        upstream.RespondWith(200, "still-here");

        await using var factory = await GatewayFactory.CreateAsync(upstream);
        var client = factory.CreateClient();

        var bad = $$"""
            {
              "routes": [{
                "id": "bad-lb",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": {
                  "loadBalancingPolicy": "NoSuchPolicy",
                  "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent(bad, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("NoSuchPolicy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var after = await client.GetAsync("/anything");
        Assert.Equal("still-here", await after.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reload_UnregisteredPlugin_Returns400_AndKeepsServingOldRoutes()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        var upstream = await Upstream();
        upstream.RespondWith(200, "still-here");

        await using var factory = await GatewayFactory.CreateAsync(upstream);
        var client = factory.CreateClient();

        var bad = $$"""
            {
              "routes": [{
                "id": "bad-route",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": [{ "name": "custom", "variant": "does-not-exist", "order": 1, "config": {} }]
              }]
            }
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent(bad, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var after = await client.GetAsync("/anything");
        Assert.Equal("still-here", await after.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reload_AtomicWrite_LeavesNoTempFiles()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        var upstream = await Upstream();
        await using var factory = await GatewayFactory.CreateAsync(upstream);
        var client = factory.CreateClient();

        var routesPath = Environment.GetEnvironmentVariable("Gateway__RoutesPath")!;
        var dir        = Path.GetDirectoryName(routesPath)!;
        var fileName   = Path.GetFileName(routesPath);

        var newRoutes = $$"""
            { "routes": [{ "id": "r", "route": { "match": { "path": "/{**rest}" } },
              "cluster": {
                "loadBalancingPolicy": "RoundRobin",
                "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                "httpRequest": { "activityTimeout": "00:00:05" }
              },
              "plugins": [] }] }
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent(newRoutes, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Empty(Directory.GetFiles(dir, fileName + ".tmp-*"));
        Assert.Equal(newRoutes, await File.ReadAllTextAsync(routesPath));
    }

    [Fact]
    public async Task Reload_EmitsAuditReloadCounter()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        var upstream = await Upstream();
        await using var factory = await GatewayFactory.CreateAsync(upstream);
        var client = factory.CreateClient();

        long reloads = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "conduitsharp.gateway.admin.reloads")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref reloads, value));
        listener.Start();

        var newRoutes = $$"""
            { "routes": [{ "id": "r", "route": { "match": { "path": "/{**rest}" } },
              "cluster": {
                "loadBalancingPolicy": "RoundRobin",
                "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                "httpRequest": { "activityTimeout": "00:00:05" }
              },
              "plugins": [] }] }
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
        {
            Content = new StringContent(newRoutes, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Admin-Key", "test-key");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, Interlocked.Read(ref reloads));
    }

    [Fact]
    public async Task InvalidateCache_ByRoute_RemovesCachedEntry_NextRequestReFetches()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestKeyHash);
        var upstream = await Upstream();

        var routes = $$"""
            {
              "routes": [{
                "id": "cached",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": [{ "name": "cache", "order": 1, "enabled": true, "config": { "ttlSeconds": 300 } }]
              }]
            }
            """;
        await using var factory = await GatewayFactory.CreateAsync(upstream, routes);
        var client = factory.CreateClient();

        await client.GetAsync("/data");
        await client.GetAsync("/data");
        Assert.Single(upstream.ReceivedRequests);

        var del = new HttpRequestMessage(HttpMethod.Delete, "/admin/cache/cached");
        del.Headers.Add("X-Admin-Key", "test-key");
        var delResponse = await client.SendAsync(del);
        Assert.Equal(HttpStatusCode.OK, delResponse.StatusCode);
        Assert.Contains("Invalidated", await delResponse.Content.ReadAsStringAsync());

        await client.GetAsync("/data");
        Assert.Equal(2, upstream.ReceivedRequests.Count);
    }

    [Fact]
    public async Task InvalidateCache_WrongKey_Returns401()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", CorrectKeyHash);
        await using var factory = await GatewayFactory.CreateAsync(await Upstream());
        var client = factory.CreateClient();

        var del = new HttpRequestMessage(HttpMethod.Delete, "/admin/cache/anything");
        del.Headers.Add("X-Admin-Key", "wrong-key");

        var response = await client.SendAsync(del);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, Entries);

        public void Dispose() { }
    }

    private sealed class CollectingLogger(string categoryName, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new LogEntry(categoryName, logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);
}

using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Security boundary tests that document current behaviour and expected hardened behaviour.
///
/// Tests marked [Fact] assert currently safe behaviour — they must pass.
/// Tests marked [Fact(Skip = "Gap: ...")] document known gaps in the security surface.
/// Each Skip message references the corresponding docs/BACKLOG.md entry so gaps are traceable.
/// Remove the Skip and assert the secure outcome once the fix is implemented.
/// </summary>
[Trait("Category", "Security")]
public sealed class SecurityHardeningTests
{

    [Fact]
    public async Task RequestBody_NormalSize_IsForwardedCorrectly()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream);
        using var client = factory.CreateClient();

        var body = new string('x', 1024);
        var response = await client.PostAsync("/api/data",
            new StringContent(body, System.Text.Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var req = Assert.Single(upstream.ReceivedRequests);
        Assert.Equal(body, req.Body);
    }

    private static string RetryRoutes(string upstreamBaseUrl) =>
        GatewayFactory.DefaultRoutes(upstreamBaseUrl)
            .Replace("\"cluster\":", "\"retry\": { \"maxAttempts\": 2 },\n              \"cluster\":");

    [Fact]
    public async Task RequestBody_ExceedsLimit_Returns413()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl));
        using var client = factory.CreateClient();

        var bigBody = new byte[10 * 1024 * 1024];
        var response = await client.PutAsync("/api/data",
            new ByteArrayContent(bigBody));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RequestBody_UnderConfiguredLimit_IsForwarded()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxRequestBodyBytes"] = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/data",
            new ByteArrayContent(new byte[512]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RequestBody_ExceedsConfiguredLimit_Returns413()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxRequestBodyBytes"] = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/api/data",
            new ByteArrayContent(new byte[2048]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RequestBody_DiskBufferBudgetExceeded_Returns503()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxRequestBodyBytes"]      = "1048576",
            ["Gateway:RequestLimits:RamBufferThresholdBytes"]  = "4096",
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"] = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/api/data",
            new ByteArrayContent(new byte[100 * 1024]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RequestBody_NoBufferConsumer_StreamsAndIgnoresBudget()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"] = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/data",
            new ByteArrayContent(new byte[4096]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(upstream.ReceivedRequests);
        Assert.Equal(4096, received.Body.Length);
    }

    [Fact]
    public async Task RequestBody_NonIdempotentMethodOnRetryRoute_StreamsAndIgnoresBudget()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"] = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/data",
            new ByteArrayContent(new byte[4096]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(upstream.ReceivedRequests);
        Assert.Equal(4096, received.Body.Length);
    }

    [Fact]
    public async Task RequestBody_LargerThanMemoryThreshold_SpillsToDiskAndForwardsIntact()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:RamBufferThresholdBytes"] = "4096",
        });
        using var client = factory.CreateClient();

        var body = string.Create(512 * 1024, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = (char)('a' + i % 26);
        });
        var response = await client.PutAsync("/api/data",
            new StringContent(body, System.Text.Encoding.ASCII, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(upstream.ReceivedRequests);
        Assert.Equal(body, received.Body);
    }

    [Fact]
    public async Task RequestBody_StreamOnly_BypassesTotalBufferBudget_Returns200()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        var routesJson = GatewayFactory.DefaultRoutes(upstream.BaseUrl)
            .Replace("\"plugins\": []", "\"streamOnly\": true,\n            \"plugins\": []");
            
        await using var factory  = await GatewayFactory.CreateAsync(upstream, routesJson, settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"] = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/data",
            new ByteArrayContent(new byte[4096]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(upstream.ReceivedRequests);
        Assert.Equal(4096, received.Body.Length);
    }

    private sealed class BodyReadingPlugin : IPipelinePlugin
    {
        public PluginName Name => PluginName.Custom;
        public string?    Variant => "body-reader";
        public string     Id => "body-reader";
        public bool       ReadsRequestBody => true;
        public string?    LastBodySeen { get; private set; }

        public async Task ExecuteAsync(HttpContext context, System.Text.Json.JsonElement config, RequestDelegate next)
        {
            context.Request.Body.Position = 0;
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            LastBodySeen = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            await next(context);
        }
    }

    [Fact]
    public async Task BodyReadingPlugin_OnPostRoute_ForcesBufferAndForwardsIntact()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        var routes = GatewayFactory.DefaultRoutes(upstream.BaseUrl)
            .Replace("\"plugins\": []",
                     "\"plugins\": [{ \"name\": \"custom\", \"variant\": \"body-reader\", \"order\": 1 }]");
        var plugin = new BodyReadingPlugin();
        await using var factory = await GatewayFactory.CreateAsync(upstream, routes, plugins: [plugin]);
        using var client = factory.CreateClient();

        var body = string.Create(256 * 1024, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = (char)('a' + i % 26);
        });
        var response = await client.PostAsync("/api/data",
            new StringContent(body, System.Text.Encoding.ASCII, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, plugin.LastBodySeen);
        var received = Assert.Single(upstream.ReceivedRequests);
        Assert.Equal(body, received.Body);
    }

    [Fact]
    public async Task StreamOnlyRoute_WithBodyReadingPlugin_FailsAtStartup()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        var routes = $$"""
            {
              "routes": [{
                "id": "stream-with-reader",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": { "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } } },
                "streamOnly": true,
                "plugins": [{ "name": "custom", "variant": "body-reader", "order": 1 }]
              }]
            }
            """;
        await using var factory = await GatewayFactory.CreateAsync(
            upstream, routes, plugins: [new BodyReadingPlugin()]);

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/x");
        });
        Assert.Contains("streamOnly", ex.ToString(), StringComparison.Ordinal);
        Assert.Contains("body", ex.ToString(), StringComparison.Ordinal);
    }

    private static string RoutesWithBodyLimit(string upstreamBaseUrl, string maxRequestBodyBytes) => $$"""
        {
          "routes": [{
            "id": "limited-route",
            "description": "Per-route body limit test",
            "route": { "match": { "path": "/{**catch-all}" } },
            "cluster": {
              "loadBalancingPolicy": "RoundRobin",
              "destinations": { "node-0": { "address": "{{upstreamBaseUrl}}" } },
              "httpRequest": { "activityTimeout": "00:00:05" }
            },
            "maxRequestBodyBytes": {{maxRequestBodyBytes}},
            "retry": { "maxAttempts": 2 },
            "plugins": []
          }]
        }
        """;

    [Fact]
    public async Task RequestBody_ExceedsRouteLimit_Returns413_EvenWhenGlobalAllows()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(
            upstream, RoutesWithBodyLimit(upstream.BaseUrl, "1024"));
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/api/data",
            new ByteArrayContent(new byte[2048]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RequestBody_RouteLimitRaisesGlobal_LargeBodyIsForwarded()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(
            upstream, RoutesWithBodyLimit(upstream.BaseUrl, "1048576"),
            settings: new Dictionary<string, string?>
            {
                ["Gateway:RequestLimits:MaxRequestBodyBytes"] = "1024",
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/data",
            new ByteArrayContent(new byte[4096]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RequestBody_RouteLimitZero_DisablesPerRequestCheck()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(
            upstream, RoutesWithBodyLimit(upstream.BaseUrl, "0"),
            settings: new Dictionary<string, string?>
            {
                ["Gateway:RequestLimits:MaxRequestBodyBytes"] = "1024",
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/data",
            new ByteArrayContent(new byte[64 * 1024]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RequestBody_UnmatchedRoute_Returns404WithoutBuffering()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(
            upstream, RoutesWithBodyLimit(upstream.BaseUrl, "1024")
                .Replace("/{**catch-all}", "/only/this/path"));
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/somewhere/else",
            new ByteArrayContent(new byte[2048]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RequestBody_BudgetIsReleased_SequentialRequestsSucceed()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"] = "6144",
        });
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsync("/api/data",
                new ByteArrayContent(new byte[4096]));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(3, upstream.ReceivedRequests.Count);
    }

    [Fact]
    public async Task RequestBody_LargeBody_DoesNotCrashGateway()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream);
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var bigBody = new byte[5 * 1024 * 1024];
        var response = await client.PostAsync("/api/data", new ByteArrayContent(bigBody));

        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.RequestEntityTooLarge,
            $"Expected 200 or 413, got {(int)response.StatusCode} — gateway must not return 5xx for large bodies.");
    }

    private static string SwaggerFetchFromRoutes(string fetchFromUrl) => $$"""
        {
          "routes": [{
            "id": "swagger-route",
            "description": "SSRF test route",
            "route": { "match": { "path": "/api/ssrf-test/{**rest}" } },
            "cluster": null,
            "swagger": { "fetchFrom": "{{fetchFromUrl}}" },
            "plugins": []
          }]
        }
        """;

    [Fact]
    public async Task SwaggerFetch_ConnectionRefused_Returns502NotCrash()
    {
        var routes = SwaggerFetchFromRoutes("http://127.0.0.1:1/openapi.json");
        await using var factory  = await GatewayFactory.CreateAsync(
            await FakeUpstream.StartAsync(), routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/swagger-route.json");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerFetch_PrivateIpRange_IsBlocked()
    {
        var routes = SwaggerFetchFromRoutes("http://169.254.169.254/latest/meta-data/");
        await using var factory  = await GatewayFactory.CreateAsync(
            await FakeUpstream.StartAsync(), routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/swagger-route.json");

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 400 or 403, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task SwaggerFetch_AllowlistedHost_IsAttempted()
    {
        var routes = SwaggerFetchFromRoutes("http://spec-host.invalid/openapi.json");
        await using var factory  = await GatewayFactory.CreateAsync(
            await FakeUpstream.StartAsync(), routes,
            settings: new Dictionary<string, string?>
            {
                ["Gateway:Swagger:AllowedSpecHosts:0"] = "spec-host.invalid",
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/swagger-route.json");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerFetch_ErrorMessage_DoesNotLeakInternalUrlDetails()
    {
        const string internalUrl = "http://127.0.0.1:1/openapi.json";
        var routes = SwaggerFetchFromRoutes(internalUrl);
        await using var factory  = await GatewayFactory.CreateAsync(
            await FakeUpstream.StartAsync(), routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/swagger-route.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain(internalUrl, body);
        Assert.DoesNotContain("127.0.0.1", body);
    }

    private static string SwaggerSpecFileRoutes(string specFile) => $$"""
        {
          "routes": [{
            "id": "spec-route",
            "description": "Path traversal test route",
            "route": { "match": { "path": "/api/spec-test/{**rest}" } },
            "cluster": null,
            "swagger": { "specFile": "{{specFile}}" },
            "plugins": []
          }]
        }
        """;

    [Fact]
    public async Task SwaggerSpec_LegitSpecFile_IsServedCorrectly()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"conduit-sec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        var specContent = """{"openapi":"3.0.0","info":{"title":"Test","version":"1.0"}}""";
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "spec.json"), specContent);

        try
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", tmpDir);
            var routes = SwaggerSpecFileRoutes("spec.json");
            await using var factory  = await GatewayFactory.CreateAsync(
                await FakeUpstream.StartAsync(), routes);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/swagger/spec-route.json");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"openapi\"", body);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", null);
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task SwaggerSpec_UnparseableUpstreamSpec_IsServedVerbatim()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"conduit-sec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        // Valid JSON the OpenAPI reader rejects (no "openapi"/"swagger" version key).
        const string upstreamSpec =
            """{"info":{"title":"Not really a spec"},"x-vendor":"keep-me","servers":[{"url":"http://internal-host:9999"}]}""";
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "spec.json"), upstreamSpec);

        try
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", tmpDir);
            var routes = """
                {
                  "routes": [{
                    "id": "odd-spec",
                    "route": { "match": { "path": "/api/odd/{**rest}" } },
                    "cluster": null,
                    "swagger": { "specFile": "spec.json" },
                    "plugins": [
                      { "name": "api-key-auth", "order": 1, "config": { "header": "X-Api-Key", "apiKey": "k" } }
                    ]
                  }]
                }
                """;
            await using var factory = await GatewayFactory.CreateAsync(
                await FakeUpstream.StartAsync(), routes);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/swagger/odd-spec.json");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // Everything we cannot model is passed through untouched ...
            Assert.Contains("x-vendor", body, StringComparison.Ordinal);
            Assert.Contains("Not really a spec", body, StringComparison.Ordinal);
            // ... except servers, which must never publish the upstream's own host.
            Assert.DoesNotContain("internal-host", body, StringComparison.Ordinal);
            Assert.DoesNotContain("9999", body, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", null);
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task SwaggerSpec_WithAuthPlugins_InjectsSecuritySchemes()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"conduit-sec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "spec.json"),
            """{"openapi":"3.0.0","info":{"title":"Test","version":"1.0"}}""");

        try
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", tmpDir);
            var routes = """
                {
                  "routes": [{
                    "id": "secured-spec",
                    "route": { "match": { "path": "/api/secure/{**rest}" } },
                    "cluster": null,
                    "swagger": { "specFile": "spec.json" },
                    "plugins": [
                      { "name": "api-key-auth", "order": 1, "config": { "header": "X-Api-Key", "apiKey": "k" } },
                      { "name": "jwt-auth",     "order": 2, "config": { "signingKey": "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo" } }
                    ]
                  }]
                }
                """;
            await using var factory = await GatewayFactory.CreateAsync(
                await FakeUpstream.StartAsync(), routes);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/swagger/secured-spec.json");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("securitySchemes", body);
            Assert.Contains("ApiKey", body);
            Assert.Contains("X-Api-Key", body);
            Assert.Contains("Bearer", body);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", null);
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task SwaggerSpec_BearerDescription_DefaultsToGeneric_AndIsConfigurable()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"conduit-sec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "spec.json"),
            """{"openapi":"3.0.0","info":{"title":"Test","version":"1.0"}}""");

        try
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", tmpDir);
            var routes = """
                {
                  "routes": [{
                    "id": "secured-spec",
                    "route": { "match": { "path": "/api/secure/{**rest}" } },
                    "cluster": null,
                    "swagger": { "specFile": "spec.json" },
                    "plugins": [
                      { "name": "jwt-auth", "order": 1, "config": { "signingKey": "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo" } }
                    ]
                  }]
                }
                """;

            await using (var factory = await GatewayFactory.CreateAsync(await FakeUpstream.StartAsync(), routes))
            {
                var body = await (await factory.CreateClient().GetAsync("/swagger/secured-spec.json"))
                    .Content.ReadAsStringAsync();
                Assert.Contains("JWT bearer token.", body);
                Assert.DoesNotContain("generate-token", body);
            }

            var settings = new Dictionary<string, string?>
            {
                ["Gateway:Swagger:BearerDescription"] = "JWT bearer token. Generate one with: pwsh generate-token.ps1"
            };
            await using (var factory = await GatewayFactory.CreateAsync(
                await FakeUpstream.StartAsync(), routes, settings: settings))
            {
                var body = await (await factory.CreateClient().GetAsync("/swagger/secured-spec.json"))
                    .Content.ReadAsStringAsync();
                Assert.Contains("generate-token.ps1", body);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", null);
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task SwaggerSpec_PathTraversal_IsBlocked()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"conduit-sec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", tmpDir);
            var routes = SwaggerSpecFileRoutes("../../../../../../etc/hosts");
            await using var factory  = await GatewayFactory.CreateAsync(
                await FakeUpstream.StartAsync(), routes);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/swagger/spec-route.json");
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest ||
                response.StatusCode == HttpStatusCode.BadGateway,
                $"Expected 400 or 502, got {(int)response.StatusCode}");
            Assert.DoesNotContain("localhost", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Gateway__BasePath", null);
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task RouteId_WithPathSeparator_IsRejectedAtStartup()
    {
        var routes = """
            {
              "routes": [{
                "id": "../../evil",
                "route": { "match": { "path": "/api/evil" } },
                "cluster": null,
                "plugins": []
              }]
            }
            """;

        var upstream = await FakeUpstream.StartAsync();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var factory = await GatewayFactory.CreateAsync(upstream, routes);
            using var client = factory.CreateClient();
        });
        Assert.NotNull(ex);
        Assert.Contains("Route IDs", ex.Message);
        await upstream.DisposeAsync();
    }

    [Fact]
    public async Task AdminApi_WithoutKeyConfigured_IsNotExposed()
    {
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", null);
        try
        {
            await using var upstream = await FakeUpstream.StartAsync();
            var routes = $$"""
                {
                  "routes": [{
                    "id": "api",
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
            using var client = factory.CreateClient();

            var response = await client.PostAsync("/admin/routes/reload",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", null);
        }
    }

    [Fact]
    public async Task AdminApi_MissingKey_Returns401()
    {
        var keyHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("admin-secret")));
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", keyHash);
        try
        {
            await using var factory = await GatewayFactory.CreateAsync(await FakeUpstream.StartAsync());
            using var client = factory.CreateClient();

            var response = await client.PostAsync("/admin/routes/reload",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", null);
        }
    }

    [Fact]
    public async Task AdminApi_WrongKey_Returns401_NotServerError()
    {
        var keyHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("admin-secret")));
        Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", keyHash);
        try
        {
            await using var factory = await GatewayFactory.CreateAsync(await FakeUpstream.StartAsync());
            using var client = factory.CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Post, "/admin/routes/reload")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Admin-Key", "wrong-key");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", null);
        }
    }
}

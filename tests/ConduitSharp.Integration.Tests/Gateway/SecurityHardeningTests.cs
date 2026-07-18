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
    // =========================================================================
    // S1 — Request body size limit
    // =========================================================================

    [Fact]
    public async Task RequestBody_NormalSize_IsForwardedCorrectly()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream);
        using var client = factory.CreateClient();

        var body = new string('x', 1024); // 1 KB
        var response = await client.PostAsync("/api/data",
            new StringContent(body, System.Text.Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var req = Assert.Single(upstream.ReceivedRequests);
        Assert.Equal(body, req.Body);
    }

    // Routes buffer only when something consumes the buffer (retry rewind or a body-reading
    // plugin) AND the method is retryable — everything else streams. Buffered-path tests use
    // a retry route + PUT (idempotent → buffers); streaming-path tests use plain routes/POST.
    private static string RetryRoutes(string upstreamBaseUrl) =>
        GatewayFactory.DefaultRoutes(upstreamBaseUrl)
            .Replace("\"cluster\":", "\"retry\": { \"maxAttempts\": 2 },\n              \"cluster\":");

    [Fact]
    public async Task RequestBody_ExceedsLimit_Returns413()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl));
        using var client = factory.CreateClient();

        var bigBody = new byte[10 * 1024 * 1024]; // 10 MB > 8 MiB default limit
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
    public async Task RequestBody_TotalBufferBudgetExceeded_Returns503()
    {
        // Per-request limit admits the body, but the shared buffering budget does not.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxRequestBodyBytes"]       = "1048576",
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"] = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/api/data",
            new ByteArrayContent(new byte[4096]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RequestBody_NoBufferConsumer_StreamsAndIgnoresBudget()
    {
        // No retry, no body-reading plugin → nothing consumes a buffer → the route streams
        // automatically. The buffering budget must not apply.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"] = "1024",
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
        // Retry route, but POST never retries — its buffer would have no consumer, so it
        // streams and the budget must not apply.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"] = "1024",
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
        // Buffered path with a body well past MemoryBufferThresholdBytes: FileBufferingReadStream
        // spills to a temp file; the forwarded bytes must be identical.
        // The threshold is pinned rather than left to the default — otherwise raising the default
        // (it is now 1 MiB) silently parks this body in memory and the spill goes untested.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MemoryBufferThresholdBytes"] = "4096",
        });
        using var client = factory.CreateClient();

        // ASCII pattern — FakeUpstream captures the body as text, so keep it encoding-safe.
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
        // streamOnly avoids BufferRequestBody, so the buffering budget is not consumed.
        await using var upstream = await FakeUpstream.StartAsync();
        var routesJson = GatewayFactory.DefaultRoutes(upstream.BaseUrl)
            .Replace("\"plugins\": []", "\"streamOnly\": true,\n            \"plugins\": []");
            
        await using var factory  = await GatewayFactory.CreateAsync(upstream, routesJson, settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"] = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/data",
            new ByteArrayContent(new byte[4096]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(upstream.ReceivedRequests);
        Assert.Equal(4096, received.Body.Length);
    }

    // A plugin that reads the body cannot run on a streamOnly route (no buffered/seekable body).
    // The gateway must reject that pairing at startup, not hand the plugin a forward-only stream.
    private sealed class BodyReadingPlugin : IPipelinePlugin
    {
        public PluginName Name => PluginName.Custom;
        public string?    Variant => "body-reader";
        public string     Id => "body-reader";
        public bool       ReadsRequestBody => true;
        public string?    LastBodySeen { get; private set; }

        public async Task ExecuteAsync(HttpContext context, System.Text.Json.JsonElement config, RequestDelegate next)
        {
            // Same contract BodyCapturePlugin relies on: seekable, rewindable, pre-buffered.
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
        // ReadsRequestBody forces the buffered path even for POST (which would otherwise
        // stream): the plugin must see the full body AND the upstream must still get it.
        // Body > MemoryBufferThresholdBytes so the read spans the disk-spill boundary.
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
        Assert.Equal(body, plugin.LastBodySeen);                      // plugin saw the whole body
        var received = Assert.Single(upstream.ReceivedRequests);
        Assert.Equal(body, received.Body);                            // upstream still got it all
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

        // 2 KB is under the 8 MiB global default but over the route's 1 KB limit.
        // PUT: buffered-path enforcement (route has retry; POST would stream).
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

        // 4 KB exceeds the 1 KB global default but the route allows up to 1 MiB.
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
        // Route matching runs before body buffering — an oversized body to an
        // unmatched path gets 404, not 413, and is never read into memory.
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
        // Budget admits one 4 KB body at a time; sequential requests must all pass,
        // proving reservations are released when a request completes.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"] = "6144",
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
        // Documents current behaviour: large bodies don't crash the gateway — they are
        // forwarded. This test should remain passing once S1 is implemented (it will
        // return 413 instead of 200, which is fine — the gateway doesn't crash either way).
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream);
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var bigBody = new byte[5 * 1024 * 1024]; // 5 MB
        var response = await client.PostAsync("/api/data", new ByteArrayContent(bigBody));

        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.RequestEntityTooLarge,
            $"Expected 200 or 413, got {(int)response.StatusCode} — gateway must not return 5xx for large bodies.");
    }

    // =========================================================================
    // S2 — SSRF in Swagger spec fetching (fetchFrom)
    // =========================================================================

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
        // Port 1 on loopback is not listening — guaranteed connection refused.
        var routes = SwaggerFetchFromRoutes("http://127.0.0.1:1/openapi.json");
        await using var factory  = await GatewayFactory.CreateAsync(
            await FakeUpstream.StartAsync(), routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/swagger-route.json");

        // Gateway must not crash or return 5xx with an unhandled exception.
        // It should return 502 Bad Gateway with an error message.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerFetch_PrivateIpRange_IsBlocked()
    {
        // AWS metadata IP — should be blocked before making any network call.
        var routes = SwaggerFetchFromRoutes("http://169.254.169.254/latest/meta-data/");
        await using var factory  = await GatewayFactory.CreateAsync(
            await FakeUpstream.StartAsync(), routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/swagger-route.json");

        // After fix: expect 400 or 403, not a real network attempt that might succeed.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 400 or 403, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task SwaggerFetch_AllowlistedHost_IsAttempted()
    {
        // A non-loopback, non-upstream host is normally refused with 403 — but when
        // listed in Gateway:Swagger:AllowedSpecHosts the fetch is attempted, surfacing
        // as 502 here because the name does not resolve.
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
        // The 502 body must stay generic: exception messages carry the target URL.
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

    // =========================================================================
    // S3 — Path traversal in Swagger specFile
    // =========================================================================

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
    public async Task SwaggerSpec_WithAuthPlugins_InjectsSecuritySchemes()
    {
        // The aggregated spec injects OpenAPI security schemes derived from the route's
        // plugin list — apiKey for api-key-auth, http bearer for jwt-auth.
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
        // The bearer scheme's description must not hardcode example-specific instructions
        // (e.g. a demo-token script) in the core library — it's a deployment-level setting.
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

            // Default — no custom description configured.
            await using (var factory = await GatewayFactory.CreateAsync(await FakeUpstream.StartAsync(), routes))
            {
                var body = await (await factory.CreateClient().GetAsync("/swagger/secured-spec.json"))
                    .Content.ReadAsStringAsync();
                Assert.Contains("JWT bearer token.", body);
                Assert.DoesNotContain("generate-token", body);
            }

            // Deployment-configured description flows through to the served spec.
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
            // Traversal: resolves to /etc/hosts (exists on macOS/Linux)
            var routes = SwaggerSpecFileRoutes("../../../../../../etc/hosts");
            await using var factory  = await GatewayFactory.CreateAsync(
                await FakeUpstream.StartAsync(), routes);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/swagger/spec-route.json");
            var body = await response.Content.ReadAsStringAsync();

            // After fix: must not serve /etc/hosts contents.
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

    // =========================================================================
    // S4 — Route ID used as filesystem directory name
    // =========================================================================

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

        // Startup must fail: WebApplicationFactory builds the host lazily, so the
        // startup validation fires on first CreateClient(), not on CreateAsync.
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

    // =========================================================================
    // S5 — Admin API hardening
    // =========================================================================

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

            // Admin path has no route match → GatewayMiddleware returns 404.
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

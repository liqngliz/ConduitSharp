using ConduitSharp.E2E.Shared;

namespace ConduitSharp.LegacyGateway.E2E.Tests;

/// <summary>
/// End-to-end tests for the LegacyGateway example: the shared gateway contract
/// (<see cref="GatewayE2ETestsBase"/>) plus what only this stack exercises — the
/// streamOnly upload route carrying body-capture-streaming.
///
/// The fixture handles: make clean → make run → gateway ready → dispose with make stop.
/// Each test uses a real HttpClient pointed at http://localhost:5050.
///
/// Run via:
///   cd examples/LegacyGateway && make test-e2e
///   dotnet test tests/ConduitSharp.LegacyGateway.E2E.Tests
/// </summary>
[Collection("LegacyGateway E2E")]
[Trait("Category", "E2E")]
public sealed class LegacyGatewayE2ETests(LegacyGatewayFixture fx) : GatewayE2ETestsBase(fx)
{
    // =========================================================================
    // Uploads — streamOnly; body-capture-streaming tees a bounded prefix off the
    // streaming path (the buffered body-capture variant is startup-rejected here)
    // =========================================================================

    [Fact]
    public async Task PostUpload_WithStreamOnly_Succeeds_AndStreamingCaptureLogsPrefixWithRouteId()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/upload/file");
        request.Content = new StringContent(
            """{"upload":"video"}""",
            Encoding.UTF8, "application/json");

        var response = await Fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var logPath = Path.Combine(Fx.ExampleRoot, "logs", "gateway.log");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        string content = "";
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(logPath))
            {
                content = await ReadSharedAsync(logPath);
                if (content.Contains("""{"upload":"video"}""") && content.Contains("upload-service"))
                    break;
            }
            await Task.Delay(500);
        }

        // The streaming tee logged the body prefix, attributed to the matched route.
        Assert.Contains("""{"upload":"video"}""", content);
        Assert.Contains("upload-service", content);

        // The buffered variant must NOT have run — this route has no rewindable body to give it.
        Assert.DoesNotContain("""Captured request body for path /api/upload/file""", content);
    }

    // =========================================================================
    // Resilience — a node crash mid-traffic is absorbed, not amplified
    // =========================================================================

    [Fact]
    public async Task Upstream_node_crash_is_absorbed_by_circuit_breaker()
    {
        // Hard-kill inventory node-1 (port 5102 — chosen because the /health route and
        // swagger fetchFrom pin node-0, so nothing else in the suite depends on this node).
        // Contract: clients may see at most a handful of 502s while the breaker counts
        // failures (threshold 2, routes.json), never a 500 or a hang; once the circuit
        // opens, round-robin converges on the survivor and stays clean for the cooldown.
        var pidFile = Path.Combine(Fx.ExampleRoot, ".pid-inv2");
        var pid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());
        Process.GetProcessById(pid).Kill();

        var statuses = new List<int>();
        for (var i = 0; i < 30; i++)
        {
            var request  = ApiKeyRequest(HttpMethod.Get, "/api/inventory");
            var response = await Fx.Client.SendAsync(request);
            statuses.Add((int)response.StatusCode);
        }

        Assert.All(statuses, status => Assert.True(status is 200 or 502,
            $"upstream death must surface as 502 or be absorbed, got {status} (sequence: {string.Join(",", statuses)})"));
        Assert.True(statuses.Count(s => s == 502) <= 5,
            $"breaker should open after 2 failures, got sequence: {string.Join(",", statuses)}");
        Assert.All(statuses.TakeLast(10), status => Assert.Equal(200, status));
    }

    // =========================================================================
    // Rate limiting — drop-in SlidingWindowRateLimiter (IRateLimiter) wired via the
    // plugins root. The /api/ratelimit-demo route carries a tiny per-client quota
    // (maxRequests: 3, windowSeconds: 30), so a 4th request inside the window trips 429.
    // =========================================================================

    [Fact]
    public async Task SlidingWindowLimiter_IsDiscovered_AndEnforcesPerClientQuota()
    {
        // The host logs the drop-in algorithm it registered at startup — proof the *sliding*
        // limiter is active, not the built-in fixed window (both would 429, only this line
        // distinguishes them).
        var logPath = Path.Combine(Fx.ExampleRoot, "logs", "gateway.log");
        var log = File.Exists(logPath) ? await ReadSharedAsync(logPath) : "";
        Assert.Contains("SlidingWindowRateLimiter", log, StringComparison.Ordinal);

        // A unique client key isolates this burst from any other caller/run against the
        // shared per-route counter.
        var clientKey = $"burst-{Guid.NewGuid():N}";
        var statuses = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/ratelimit-demo/probe");
            request.Headers.Add("X-Demo-Client", clientKey);
            statuses.Add(await Fx.Client.SendAsync(request));
        }

        // First 3 pass the limiter (forwarded upstream — any non-429 status); the 4th is denied.
        Assert.All(statuses.Take(3), r => Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3].StatusCode);

        // The algorithm supplies its own Retry-After — a sliding log answers "seconds until your
        // oldest request ages out", and it must be a positive whole number of seconds.
        var retryAfter = statuses[3].Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter!.Value.TotalSeconds, 1, 30);
    }
}

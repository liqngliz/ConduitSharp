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

        Assert.Contains("""{"upload":"video"}""", content);
        Assert.Contains("upload-service", content);

        Assert.DoesNotContain("""Captured request body for path /api/upload/file""", content);
    }

    [Fact]
    public async Task Upstream_node_crash_is_absorbed_by_circuit_breaker()
    {
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

    [Fact]
    public async Task SlidingWindowLimiter_IsDiscovered_AndEnforcesPerClientQuota()
    {
        var logPath = Path.Combine(Fx.ExampleRoot, "logs", "gateway.log");
        var log = File.Exists(logPath) ? await ReadSharedAsync(logPath) : "";
        Assert.Contains("SlidingWindowRateLimiter", log, StringComparison.Ordinal);

        var clientKey = $"burst-{Guid.NewGuid():N}";
        var statuses = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/ratelimit-demo/probe");
            request.Headers.Add("X-Demo-Client", clientKey);
            statuses.Add(await Fx.Client.SendAsync(request));
        }

        Assert.All(statuses.Take(3), r => Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3].StatusCode);

        var retryAfter = statuses[3].Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter!.Value.TotalSeconds, 1, 30);
    }
}

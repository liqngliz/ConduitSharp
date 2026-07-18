using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Verifies gateway error handling when the upstream is unreachable, slow,
/// or the client disconnects mid-request.
/// </summary>
public sealed class UpstreamErrorTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    [Fact]
    public async Task ConnectionRefused_Returns502()
    {
        // Port 1 on loopback is not listening — guaranteed connection refused.
        var routes = GatewayTestHelpers.CatchAllRoutes(
            _upstream.BaseUrl, upstreamOverride: "http://127.0.0.1:1");
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/anything");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task UpstreamTimeout_Returns504()
    {
        _upstream.RespondWith(async ctx =>
            await Task.Delay(TimeSpan.FromSeconds(30), ctx.RequestAborted));

        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, upstreamHttpTimeout: TimeSpan.FromMilliseconds(150));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/slow");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task RouteTimeoutMs_IsEnforced_Returns504()
    {
        // The route's own timeoutMs (not an HttpClient override) must bound the upstream:
        // a 30 s upstream against a 300 ms route timeout returns 504 well under 30 s.
        _upstream.RespondWith(async ctx =>
            await Task.Delay(TimeSpan.FromSeconds(30), ctx.RequestAborted));

        var routes = $$"""
            {
              "routes": [{
                "id": "timeout-route",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:00.300" }
                },
                "plugins": []
              }]
            }
            """;
        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();

        var sw       = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.GetAsync("/slow");
        sw.Stop();

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.True(sw.ElapsedMilliseconds < 10_000,
            $"Took {sw.ElapsedMilliseconds} ms — the 300 ms route timeout did not fire.");
    }

    [Fact]
    public async Task ClientCancels_ThrowsOnClientSide()
    {
        _upstream.RespondWith(async ctx =>
            await Task.Delay(TimeSpan.FromSeconds(30), ctx.RequestAborted));

        await using var factory = await GatewayFactory.CreateAsync(_upstream);
        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("/slow", cts.Token));
    }

    // -------------------------------------------------------------------------
    // R2 — upstream retry on transient failure
    // -------------------------------------------------------------------------

    private string RoutesWithRetry(int maxAttempts, string methods = "[]") => $$"""
        {
          "routes": [{
            "id": "retry-route",
            "route": { "match": { "path": "/{**rest}", "methods": {{methods}} } },
            "cluster": {
              "loadBalancingPolicy": "RoundRobin",
              "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } },
              "httpRequest": { "activityTimeout": "00:00:05" }
            },
            "retry": { "maxAttempts": {{maxAttempts}} },
            "plugins": []
          }]
        }
        """;

    // Fails the first `failures` calls with 503, then serves 200.
    private void FailThenSucceed(int failures)
    {
        var calls = 0;
        _upstream.RespondWith(async ctx =>
        {
            var n = Interlocked.Increment(ref calls);
            if (n <= failures) { ctx.Response.StatusCode = 503; return; }
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("ok");
        });
    }

    [Fact]
    public async Task TransientUpstreamFailure_IsRetried_ReturnsSuccess()
    {
        FailThenSucceed(failures: 1);
        await using var factory = await GatewayFactory.CreateAsync(_upstream, RoutesWithRetry(maxAttempts: 2));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        Assert.Equal(2, _upstream.ReceivedRequests.Count); // one failed, one retried
    }

    [Fact]
    public async Task NoRetryConfigured_TransientFailure_IsNotRetried()
    {
        FailThenSucceed(failures: 1);
        await using var factory = await GatewayFactory.CreateAsync(_upstream, RoutesWithRetry(maxAttempts: 1));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RetriesExhausted_ReturnsLastResponse()
    {
        FailThenSucceed(failures: 5); // always fails within the attempt budget
        await using var factory = await GatewayFactory.CreateAsync(_upstream, RoutesWithRetry(maxAttempts: 3));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, _upstream.ReceivedRequests.Count); // 1 initial + 2 retries
    }

    [Fact]
    public async Task NonIdempotentMethod_IsNotRetried_EvenWhenConfigured()
    {
        // POST may already have been processed upstream — retrying could double-apply it.
        FailThenSucceed(failures: 1);
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, RoutesWithRetry(maxAttempts: 3, methods: """["GET","POST"]"""));
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/data",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests); // no retry for POST
    }

    // -------------------------------------------------------------------------
    // upstream.retry block — the full policy surface (maxAttempts / backoff / retryOn)
    // -------------------------------------------------------------------------

    private string RoutesWithRetryBlock(string retryJson) => $$"""
        {
          "routes": [{
            "id": "retry-block-route",
            "route": { "match": { "path": "/{**rest}" } },
            "cluster": {
              "loadBalancingPolicy": "RoundRobin",
              "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } },
              "httpRequest": { "activityTimeout": "00:00:05" }
            },
            "retry": {{retryJson}},
            "plugins": []
          }]
        }
        """;

    [Fact]
    public async Task RetryBlock_MaxAttemptsWithBackoff_RetriesUntilSuccess()
    {
        FailThenSucceed(failures: 2);
        await using var factory = await GatewayFactory.CreateAsync(_upstream,
            RoutesWithRetryBlock("""{ "maxAttempts": 3, "delayMs": 1, "backoff": "Exponential", "jitter": true }"""));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        Assert.Equal(3, _upstream.ReceivedRequests.Count); // two failed, third served
    }

    [Fact]
    public async Task RetryBlock_RetryOn_CustomStatusCode_IsRetried()
    {
        // 500 is not retryable by default; retryOn opts it in.
        var calls = 0;
        _upstream.RespondWith(async ctx =>
        {
            if (Interlocked.Increment(ref calls) == 1) { ctx.Response.StatusCode = 500; return; }
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("recovered");
        });

        await using var factory = await GatewayFactory.CreateAsync(_upstream,
            RoutesWithRetryBlock("""{ "maxAttempts": 2, "retryOn": [500] }"""));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("recovered", await response.Content.ReadAsStringAsync());
        Assert.Equal(2, _upstream.ReceivedRequests.Count);
    }

    [Fact]
    public async Task RetryBlock_StatusOutsideRetryOn_IsNotRetried()
    {
        FailThenSucceed(failures: 1); // fails with 503
        await using var factory = await GatewayFactory.CreateAsync(_upstream,
            RoutesWithRetryBlock("""{ "maxAttempts": 3, "retryOn": [502] }"""));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests); // 503 not in retryOn — passed through
    }

    [Fact]
    public async Task IdempotentMethodWithBody_RetryResendsFullBody()
    {
        // Regression: each attempt's HttpRequestMessage is now disposed, so the retry
        // must rebuild its content from the buffered body rather than depend on the
        // first attempt's (previously leaked) message keeping the stream open.
        FailThenSucceed(failures: 1);
        await using var factory = await GatewayFactory.CreateAsync(_upstream, RoutesWithRetry(maxAttempts: 2));
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/api/data",
            new StringContent("""{"v":42}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _upstream.ReceivedRequests.Count);
        Assert.All(_upstream.ReceivedRequests, r => Assert.Equal("""{"v":42}""", r.Body));
    }
}

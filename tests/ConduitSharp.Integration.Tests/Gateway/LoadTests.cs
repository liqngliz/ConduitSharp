using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Validates that the gateway remains correct and stable under concurrent and sustained load.
/// All tests use in-process WebApplicationFactory — no external collector needed.
///
/// These tests are intentionally conservative (no auth plugins, no rate limiting) so they
/// measure pipeline throughput without interference from business-logic short-circuits.
/// </summary>
[Trait("Category", "Load")]
public sealed class LoadTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;
    private GatewayFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _upstream = await FakeUpstream.StartAsync();
        _factory  = await GatewayFactory.CreateAsync(_upstream);
        _client   = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _upstream.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Concurrency
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentRequests_100Parallel_AllReturn200()
    {
        const int count = 100;

        var tasks = Enumerable
            .Range(0, count)
            .Select(i => _client.GetAsync($"/api/item/{i}"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(count, _upstream.ReceivedRequests.Count);
    }

    [Fact]
    public async Task ConcurrentPostRequests_50Parallel_AllForwardBody()
    {
        const int count = 50;
        var content = """{"value":42}""";

        var tasks = Enumerable
            .Range(0, count)
            .Select(_ => _client.PostAsync("/api/data",
                new StringContent(content, System.Text.Encoding.UTF8, "application/json")))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(count, _upstream.ReceivedRequests.Count);
        Assert.All(_upstream.ReceivedRequests, req => Assert.Equal(content, req.Body));
    }

    [Fact]
    public async Task ConcurrentMixedMethods_NoDeadlock()
    {
        const int perVerb = 30;

        var gets  = Enumerable.Range(0, perVerb).Select(_ => _client.GetAsync("/api/resource"));
        var posts = Enumerable.Range(0, perVerb).Select(_ => _client.PostAsync("/api/resource",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json")));
        var deletes = Enumerable.Range(0, perVerb).Select(_ =>
            _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/resource")));

        var responses = await Task.WhenAll(gets.Concat(posts).Concat(deletes));

        Assert.Equal(perVerb * 3, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    // -------------------------------------------------------------------------
    // Sustained throughput
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SequentialRequests_300_AllReturn200()
    {
        const int count = 300;

        for (var i = 0; i < count; i++)
        {
            var response = await _client.GetAsync($"/api/item/{i}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(count, _upstream.ReceivedRequests.Count);
    }

    [Fact]
    public async Task SustainedLoad_CompletesWithinBudget()
    {
        const int count = 200;
        const int budgetMs = 15_000;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, count).Select(i => _client.GetAsync($"/load/{i}")).ToArray();
        var responses = await Task.WhenAll(tasks);
        sw.Stop();

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.True(sw.ElapsedMilliseconds < budgetMs,
            $"{count} concurrent requests took {sw.ElapsedMilliseconds}ms (budget: {budgetMs}ms)");
    }

    // -------------------------------------------------------------------------
    // Memory stability
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MemoryStableUnderLoad_NoUnboundedGrowth()
    {
        // Warm up to stabilise the heap before taking a baseline.
        for (var i = 0; i < 20; i++)
            await _client.GetAsync("/warmup");
        _upstream.Reset();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        const int count = 300;
        for (var i = 0; i < count; i++)
        {
            var response = await _client.GetAsync($"/api/item/{i}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        var after = GC.GetTotalMemory(forceFullCollection: true);

        const long maxGrowthBytes = 50 * 1024 * 1024; // 50 MB
        var growthBytes = after - before;
        Assert.True(growthBytes < maxGrowthBytes,
            $"Heap grew by {growthBytes / 1024 / 1024} MB after {count} requests (limit: 50 MB). " +
            "This suggests an unbounded buffer or missing disposal.");
    }

    // -------------------------------------------------------------------------
    // Concurrency through auth plugin
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentRequestsThroughApiKeyAuth_AllAuthenticateCorrectly()
    {
        const string key = "load-test-key";

        await using var upstream = await FakeUpstream.StartAsync();
        var routes = GatewayTestHelpers.RouteWithPlugin(
            upstream.BaseUrl, "api-key-auth",
            new { header = "X-Api-Key", keys = new[] { key } });
        await using var factory = await GatewayFactory.CreateAsync(upstream, routes);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        const int count = 60;
        var tasks = Enumerable.Range(0, count).Select(_ => client.GetAsync("/api/data")).ToArray();
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task ConcurrentUnauthedRequests_AllReturn401_NoUpstreamLeakage()
    {
        const string key = "secret-key";

        await using var upstream = await FakeUpstream.StartAsync();
        var routes = GatewayTestHelpers.RouteWithPlugin(
            upstream.BaseUrl, "api-key-auth",
            new { header = "X-Api-Key", keys = new[] { key } });
        await using var factory = await GatewayFactory.CreateAsync(upstream, routes);
        using var client = factory.CreateClient();

        const int count = 60;
        var tasks = Enumerable.Range(0, count).Select(_ => client.GetAsync("/api/data")).ToArray();
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Empty(upstream.ReceivedRequests);
    }
}

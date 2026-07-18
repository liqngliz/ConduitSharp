using ConduitSharp.Integration.Tests.Fixtures;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// The buffering step-down, driven end to end: RAM while the memory tier has room, temp-file spill
/// once it does not, 503 only when the combined budget is gone. The point of the tiers is that the
/// middle rung *serves the request* — a full memory tier must degrade latency, never availability.
/// </summary>
[Trait("Category", "Security")]
public sealed class BufferTierStepDownTests
{
    private static string RetryRoutes(string upstreamBaseUrl) =>
        GatewayFactory.DefaultRoutes(upstreamBaseUrl)
            .Replace("\"cluster\":", "\"retry\": { \"maxAttempts\": 2 },\n              \"cluster\":");

    // ASCII so FakeUpstream can capture the body as text.
    private static string Body(int size) => string.Create(size, 0, (span, _) =>
    {
        for (var i = 0; i < span.Length; i++) span[i] = (char)('a' + i % 26);
    });

    private static StringContent Content(string body) =>
        new(body, System.Text.Encoding.ASCII, "text/plain");

    [Fact]
    public async Task MemoryTierDisabled_BodyStillServed_FromDisk()
    {
        // Memory tier off entirely → every body spills from the first byte. Slower, still a 200.
        // This is the rung that must not turn into a 503.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxMemoryBufferedBodyBytes"] = "0",
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"]  = "10485760",
        });
        using var client = factory.CreateClient();

        var body = Body(256 * 1024);
        var response = await client.PutAsync("/api/data", Content(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, Assert.Single(upstream.ReceivedRequests).Body);
    }

    [Fact]
    public async Task MemoryTierTooSmallForThreshold_BodyStillServed_FromDisk()
    {
        // Memory tier smaller than the 4 KiB minimum threshold → no request can claim RAM, so
        // everything spills. Availability must be unaffected.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxMemoryBufferedBodyBytes"] = "512",
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"]  = "10485760",
        });
        using var client = factory.CreateClient();

        var body = Body(64 * 1024);
        var response = await client.PutAsync("/api/data", Content(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, Assert.Single(upstream.ReceivedRequests).Body);
    }

    [Fact]
    public async Task BodyOutgrowsItsMemoryThreshold_SpillsMidBody_AndForwardsIntact()
    {
        // Crosses the memory→disk boundary *during* the read, which is where the accounting is
        // easiest to get wrong: the rented buffer goes back to the pool and the memory reservation
        // is released while the request is still in flight.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MemoryBufferThresholdBytes"] = "4096",
            ["Gateway:RequestLimits:MaxMemoryBufferedBodyBytes"] = "1048576",
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"]  = "10485760",
        });
        using var client = factory.CreateClient();

        var body = Body(256 * 1024); // 64x the threshold → guaranteed spill mid-read
        var response = await client.PutAsync("/api/data", Content(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, Assert.Single(upstream.ReceivedRequests).Body);
    }

    [Fact]
    public async Task SpilledRequests_ReleaseTheirMemoryReservation_SoLaterRequestsStillGetRam()
    {
        // The leak that would quietly undo the whole design: if the memory reservation were not
        // released on spill (or in the finally), the tier would drain over time and every request
        // would end up on disk, with nothing failing loudly to say so.
        // A tier sized for exactly one in-flight body proves reuse — sequential requests can only
        // all succeed if each hands its RAM back.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MemoryBufferThresholdBytes"] = "65536",
            ["Gateway:RequestLimits:MaxMemoryBufferedBodyBytes"] = "65536", // exactly one body's worth
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"]  = "10485760",
        });
        using var client = factory.CreateClient();

        var body = Body(256 * 1024); // outgrows the threshold → spills → must release its RAM
        for (var i = 0; i < 5; i++)
        {
            var response = await client.PutAsync("/api/data", Content(body));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(5, upstream.ReceivedRequests.Count);
        Assert.All(upstream.ReceivedRequests, r => Assert.Equal(body, r.Body));
    }

    [Fact]
    public async Task TotalBudgetExhausted_Returns503_EvenWithMemoryTierHeadroom()
    {
        // The last rung. A generous memory tier must not let a request past the combined budget —
        // the total is the backstop, and the memory tier is carved out of it, not added to it.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxRequestBodyBytes"]        = "1048576",
            ["Gateway:RequestLimits:MaxMemoryBufferedBodyBytes"] = "1048576",
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"]  = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/api/data", new ByteArrayContent(new byte[4096]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task SpillDirectory_WhenConfigured_IsUsedInsteadOfSystemTemp()
    {
        // Worth pinning: in containers /tmp is often tmpfs, so "spilling" there is still RAM and
        // the disk tier silently becomes a second memory tier. Operators need this to actually work.
        var spillDir = Path.Combine(Path.GetTempPath(), "conduitsharp-spill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(spillDir);
        try
        {
            await using var upstream = await FakeUpstream.StartAsync();
            await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
            {
                ["Gateway:RequestLimits:MemoryBufferThresholdBytes"] = "4096",
                ["Gateway:RequestLimits:MaxMemoryBufferedBodyBytes"] = "0", // force the spill
                ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"]  = "10485760",
                ["Gateway:RequestLimits:SpillDirectory"]             = spillDir,
            });
            using var client = factory.CreateClient();

            var body = Body(256 * 1024);
            var response = await client.PutAsync("/api/data", Content(body));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(body, Assert.Single(upstream.ReceivedRequests).Body);
        }
        finally
        {
            Directory.Delete(spillDir, recursive: true);
        }
    }

    [Fact]
    public async Task SpillDirectory_PointedAtNothing_FailsTheSpill()
    {
        // The positive test above cannot prove the setting was honoured — the spill file is deleted
        // on dispose, so an ignored setting would pass it just as happily by quietly using the
        // system temp path. This is the half that actually binds: an unusable spill directory must
        // surface, not silently fall back.
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MemoryBufferThresholdBytes"] = "4096",
            ["Gateway:RequestLimits:MaxMemoryBufferedBodyBytes"] = "0", // force the spill
            ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"]  = "10485760",
            ["Gateway:RequestLimits:SpillDirectory"]             = Path.Combine(Path.GetTempPath(), "conduitsharp-does-not-exist-" + Guid.NewGuid().ToString("N")),
        });
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/api/data", Content(Body(256 * 1024)));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }
}

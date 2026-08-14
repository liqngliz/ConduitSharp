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

    private static string Body(int size) => string.Create(size, 0, (span, _) =>
    {
        for (var i = 0; i < span.Length; i++) span[i] = (char)('a' + i % 26);
    });

    private static StringContent Content(string body) =>
        new(body, System.Text.Encoding.ASCII, "text/plain");

    [Fact]
    public async Task MemoryTierDisabled_BodyStillServed_FromDisk()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxRamBufferedBodyBytes"] = "0",
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"]  = "10485760",
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
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxRamBufferedBodyBytes"] = "512",
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"]  = "10485760",
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
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:RamBufferThresholdBytes"] = "4096",
            ["Gateway:RequestLimits:MaxRamBufferedBodyBytes"] = "1048576",
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"]  = "10485760",
        });
        using var client = factory.CreateClient();

        var body = Body(256 * 1024);
        var response = await client.PutAsync("/api/data", Content(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, Assert.Single(upstream.ReceivedRequests).Body);
    }

    [Fact]
    public async Task SpilledRequests_ReleaseTheirMemoryReservation_SoLaterRequestsStillGetRam()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:RamBufferThresholdBytes"] = "65536",
            ["Gateway:RequestLimits:MaxRamBufferedBodyBytes"] = "65536",
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"]  = "10485760",
        });
        using var client = factory.CreateClient();

        var body = Body(256 * 1024);
        for (var i = 0; i < 5; i++)
        {
            var response = await client.PutAsync("/api/data", Content(body));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(5, upstream.ReceivedRequests.Count);
        Assert.All(upstream.ReceivedRequests, r => Assert.Equal(body, r.Body));
    }

    [Fact]
    public async Task DiskBudgetExhausted_Returns503_WhenTheBodyMustSpill()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:MaxRequestBodyBytes"]      = "1048576",
            ["Gateway:RequestLimits:RamBufferThresholdBytes"]  = "4096",
            ["Gateway:RequestLimits:MaxRamBufferedBodyBytes"]  = "1048576",
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"] = "1024",
        });
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/api/data", new ByteArrayContent(new byte[100 * 1024]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }

    [Fact]
    public async Task SpillDirectory_WhenConfigured_IsUsedInsteadOfSystemTemp()
    {
        var spillDir = Path.Combine(Path.GetTempPath(), "conduitsharp-spill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(spillDir);
        try
        {
            await using var upstream = await FakeUpstream.StartAsync();
            await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
            {
                ["Gateway:RequestLimits:RamBufferThresholdBytes"] = "4096",
                ["Gateway:RequestLimits:MaxRamBufferedBodyBytes"] = "0",
                ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"]  = "10485760",
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
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl), settings: new Dictionary<string, string?>
        {
            ["Gateway:RequestLimits:RamBufferThresholdBytes"] = "4096",
            ["Gateway:RequestLimits:MaxRamBufferedBodyBytes"] = "0",
            ["Gateway:RequestLimits:MaxDiskBufferedBodyBytes"]  = "10485760",
            ["Gateway:RequestLimits:SpillDirectory"]             = Path.Combine(Path.GetTempPath(), "conduitsharp-does-not-exist-" + Guid.NewGuid().ToString("N")),
        });
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/api/data", Content(Body(256 * 1024)));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(upstream.ReceivedRequests);
    }
}

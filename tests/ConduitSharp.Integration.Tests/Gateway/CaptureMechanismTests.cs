using System.Net.Http;
using ConduitSharp.E2E.Shared;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Fast CI-gated companion to the 500 MB CaptureMemoryMatrixTests: proves the unified body-capture
/// plugin picks its path by route shape at a 4 MB body. The heavy 500 MB measurement lives in
/// ConduitSharp.BodyCaptureMemory.E2E.Tests (Category=HeavyMemory), excluded from CI.
/// </summary>
public sealed class CaptureMechanismTests
{
    private const long Body = 4 * 1024 * 1024;

    private static async Task<(MemoryReading Reading, int Calls, HttpStatusCode Status, int Captured)> RunAsync(
        HttpMethod method, bool retry = false, int failFirst = 0, string mediaType = "text/plain")
    {
        await using var upstream = await DiscardUpstream.StartAsync();
        upstream.FailFirst = failFirst;
        var routes = CaptureRoutes.Build(upstream.BaseUrl, CaptureStyle.Capture, maxSize: 4096, retry: retry);
        await using var gateway = await CaptureGatewayHost.StartAsync(routes, CaptureStyle.Capture);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
        var sampler = new MemorySampler(gateway.SpillDir);
        sampler.Start();
        using var req = new HttpRequestMessage(method, gateway.BaseUrl + "/data")
        {
            Content = new StreamingContent(Body, mediaType: mediaType),
        };
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        return (sampler.Stop(), upstream.RequestCount, resp.StatusCode, gateway.CaptureRecords);
    }

    [Fact]
    public async Task StreamingRoute_DoesNotForceBuffering_NoSpill()
    {
        var r = await RunAsync(HttpMethod.Post, mediaType: "text/plain");

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.True(r.Reading.PeakSpillBytes < 512 * 1024,
            $"streaming capture should not spill; spilled {r.Reading.PeakSpillMB:F2} MB");
    }

    [Fact]
    public async Task RetryRoute_ReusesBuffer_CapturesBinary_AndReplays()
    {
        var r = await RunAsync(HttpMethod.Put, retry: true, failFirst: 1, mediaType: "application/octet-stream");

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(2, r.Calls);
        Assert.True(r.Captured > 0, "reuse branch should capture a binary body");
    }
}

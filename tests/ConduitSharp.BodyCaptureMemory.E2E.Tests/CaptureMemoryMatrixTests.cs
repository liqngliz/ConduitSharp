using System.Net.Http;
using ConduitSharp.E2E.Shared;
using Xunit.Abstractions;

namespace ConduitSharp.BodyCaptureMemory.E2E.Tests;

/// <summary>
/// Memory / disk / time behaviour of the unified body-capture plugin (BodyCapturePlugin,
/// variant "body-capture") under a 500 MB body. There is one plugin; the route's shape decides its
/// path:
///
///   streaming route (no retry) → tees a bounded prefix via HttpLogging (RAM = prefix, no disk;
///                                text media types only)
///   retry route (body already buffered) → reuses that seekable buffer, reads the prefix off it
///                                (no second copy; captures binary too)
///
/// Safety headline: at a bounded prefix the plugin is cheap either way. Told to capture the FULL body
/// on a streaming route it holds all of it in RAM (no disk tier) — the OOM path the 32 KiB default
/// ceiling exists to prevent, which is why the raised ceiling is a deliberate, budget-shed-guarded knob.
///
/// RSS is single-process and noisy; the sharp signals are PeakSpill (disk) and CaptureRecords (proof
/// the capture path actually ran — HttpLogging silently no-ops when its log level is disabled).
/// </summary>
[Trait("Category", "E2E")]         // excluded from `make test` (Category!=E2E)
[Trait("Category", "HeavyMemory")] // and self-documents the 500 MB weight
public sealed class CaptureMemoryMatrixTests
{
    private const long MB = 1024 * 1024;
    private const long LargeBody = 500 * MB;

    private readonly ITestOutputHelper _out;
    public CaptureMemoryMatrixTests(ITestOutputHelper output) => _out = output;

    private sealed record RunResult(MemoryReading Reading, int UpstreamCalls, HttpStatusCode Status, int CaptureRecords);

    private async Task<RunResult> RunAsync(
        CaptureStyle style, long bodyBytes, HttpMethod method,
        long memThreshold = MB, bool retry = false, int failFirst = 0, int maxSize = 4096,
        string mediaType = "application/octet-stream")
    {
        await using var upstream = await DiscardUpstream.StartAsync();
        upstream.FailFirst = failFirst;

        var routes = CaptureRoutes.Build(upstream.BaseUrl, style, maxSize: maxSize, retry: retry);
        // Capturing the FULL body needs the 32 KiB ceiling lifted to the body size.
        int? ceiling = style == CaptureStyle.Capture && maxSize > 32 * 1024 ? maxSize : null;
        await using var gateway = await CaptureGatewayHost.StartAsync(
            routes, style, memoryBufferThresholdBytes: memThreshold, streamingMaxCaptureBytes: ceiling);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var sampler = new MemorySampler(gateway.SpillDir);

        sampler.Start();
        using var req = new HttpRequestMessage(method, gateway.BaseUrl + "/data")
        {
            Content = new StreamingContent(bodyBytes, mediaType: mediaType),
        };
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        var reading = sampler.Stop();

        return new RunResult(reading, upstream.RequestCount, resp.StatusCode, gateway.CaptureRecords);
    }

    private void Report(string label, RunResult r) =>
        _out.WriteLine($"{label,-30} {r.Reading} | upstream x{r.UpstreamCalls} | captured {r.CaptureRecords} | {(int)r.Status}");

    // 1 — streaming route, bounded prefix: the tee path. Cheap, no disk, captures the text prefix.
    [Fact]
    public async Task StreamingRoute_PrefixCapture_TeesCheap_NoDisk()
    {
        var r = await RunAsync(CaptureStyle.Capture, LargeBody, HttpMethod.Post, mediaType: "text/plain");
        Report("stream prefix (tee)", r);

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.True(r.CaptureRecords > 0, "tee captured nothing — HttpLogging did not run");
        Assert.True(r.Reading.PeakSpillMB < 5,  $"tee should not spill; spilled {r.Reading.PeakSpillMB:F1} MB");
        Assert.True(r.Reading.PeakRssMB  < 64,  $"tee prefix should stay small; RSS {r.Reading.PeakRssMB:F1} MB");
    }

    // 2 — retry route, bounded prefix, BINARY body: proves the REUSE path ran. The tee skips binary,
    // so a positive capture count on octet-stream can only come from the seekable-buffer reuse branch.
    [Fact]
    public async Task RetryRoute_ReusesBuffer_CapturesBinary_AndReplays()
    {
        var r = await RunAsync(CaptureStyle.Capture, LargeBody, HttpMethod.Put,
            retry: true, failFirst: 1, mediaType: "application/octet-stream");
        Report("retry reuse (binary)", r);

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(2, r.UpstreamCalls); // body replayed
        Assert.True(r.CaptureRecords > 0,
            "reuse branch should capture a binary body the tee would have skipped");
    }

    // 3 — SAFETY: full-body capture on a streaming route holds the entire body in RAM (no disk tier).
    // RSS itself proves the tee buffered the body — 500 MB resident with zero spill.
    [Fact]
    public async Task FullCapture_StreamingRoute_HoldsWholeBodyInRam()
    {
        const int Full = (int)LargeBody;
        var r = await RunAsync(CaptureStyle.Capture, LargeBody, HttpMethod.Post,
            maxSize: Full, mediaType: "text/plain");
        Report("fullcap stream (tee)", r);

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.True(r.Reading.PeakSpillMB < 50, $"tee has no disk tier; spilled {r.Reading.PeakSpillMB:F1} MB");
        Assert.True(r.Reading.PeakRssMB > 300,  $"full capture should be RAM-resident; RSS {r.Reading.PeakRssMB:F1} MB");
    }

    // 5 — gateway buffer clamp is now configurable (no capture plugin needed): a retry route with a
    // raised MemoryBufferThreshold keeps the whole body in RAM instead of spilling. Proves the lift.
    [Fact]
    public async Task RaisedBufferThreshold_KeepsBodyInRam_SkipsSpill()
    {
        var r = await RunAsync(CaptureStyle.None, LargeBody, HttpMethod.Put,
            memThreshold: 600 * MB, retry: true);
        Report("raised threshold (retry)", r);

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.True(r.Reading.PeakSpillMB < 50, $"raised threshold should not spill; spilled {r.Reading.PeakSpillMB:F1} MB");
        Assert.True(r.Reading.PeakRssMB > 300,  $"body should be RAM-resident; RSS {r.Reading.PeakRssMB:F1} MB");
    }
}

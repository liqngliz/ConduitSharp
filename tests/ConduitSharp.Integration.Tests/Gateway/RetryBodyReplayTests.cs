using ConduitSharp.Integration.Tests.Fixtures;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// A retry must replay the *same body*, not an empty one.
///
/// This is the property the whole buffered path exists to provide, and until now nothing asserted
/// it: the retry tests all used GET, which has no body to get wrong. A gateway that streams the
/// body and then retries sends attempt 2 over a stream attempt 1 already drained — measured
/// against Ocelot with a hand-added Polly retry, that produces "Sent 0 request content bytes, but
/// Content-Length promised 4096" and a 502. Silent truncation is the worse version of the same
/// bug: the upstream gets a well-formed request with the wrong content.
///
/// It is also the regression these tests owe the buffering code. Skipping the eager read pass in
/// BufferRequestBody measures ~10% faster and breaks exactly this, in exactly the way no other
/// test in the suite would notice.
/// </summary>
[Trait("Category", "Security")]
public sealed class RetryBodyReplayTests
{
    private static string RetryRoutes(string upstreamBaseUrl) =>
        GatewayFactory.DefaultRoutes(upstreamBaseUrl)
            .Replace("\"cluster\":", "\"retry\": { \"maxAttempts\": 2 },\n              \"cluster\":");

    private static string Body(int size) => string.Create(size, 0, (span, _) =>
    {
        for (var i = 0; i < span.Length; i++) span[i] = (char)('a' + i % 26);
    });

    [Fact]
    public async Task Retry_AfterUpstreamFails_ReplaysTheBodyIntact()
    {
        await using var upstream = await FakeUpstream.StartAsync();

        // Fail the first attempt so the retry has to fire, then succeed — recording what each
        // attempt actually carried rather than only that it arrived.
        var attempts = 0;
        var carried = new List<long>();
        upstream.RespondWith(async ctx =>
        {
            var n = Interlocked.Increment(ref attempts);
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            lock (carried) carried.Add(ms.Length);

            ctx.Response.StatusCode = n == 1 ? 503 : 200;
            await ctx.Response.WriteAsync(n == 1 ? "fail once" : "ok");
        });

        await using var factory = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl));
        using var client = factory.CreateClient();

        var body = Body(64 * 1024); // over the 4 KiB floor, so it is a real buffered body
        var response = await client.PutAsync("/api/data",
            new StringContent(body, System.Text.Encoding.ASCII, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attempts); // the retry fired at all
        Assert.All(carried, len => Assert.Equal(body.Length, len)); // ...and both attempts were whole
        Assert.All(upstream.ReceivedRequests, r => Assert.Equal(body, r.Body));
    }

    [Fact]
    public async Task Retry_AfterSpill_ReplaysTheBodyIntactFromDisk()
    {
        // The same guarantee once the body has left memory. Rewinding a FileBufferingReadStream
        // that spilled means re-reading the temp file, so this covers a different code path than
        // the RAM case above — and it is the path a large upload actually takes.
        await using var upstream = await FakeUpstream.StartAsync();

        var attempts = 0;
        upstream.RespondWith(async ctx =>
        {
            var n = Interlocked.Increment(ref attempts);
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            ctx.Response.StatusCode = n == 1 ? 503 : 200;
            await ctx.Response.WriteAsync("x");
        });

        await using var factory = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl),
            settings: new Dictionary<string, string?>
            {
                ["Gateway:RequestLimits:MemoryBufferThresholdBytes"] = "4096", // force the spill
                ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"]  = "10485760",
            });
        using var client = factory.CreateClient();

        var body = Body(256 * 1024); // 64x the threshold — spills well before the end
        var response = await client.PutAsync("/api/data",
            new StringContent(body, System.Text.Encoding.ASCII, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attempts);
        Assert.All(upstream.ReceivedRequests, r => Assert.Equal(body, r.Body));
    }

    [Fact]
    public async Task Post_OnARetryRoute_IsNeverReplayed_SoItsBodyIsNeverAtRisk()
    {
        // The other half of the guarantee: ConduitSharp's retries are method-aware, so a POST on a
        // retry route streams and is never replayed. That is what makes streaming it safe — an
        // un-rewindable body is only a hazard for a request something might retry.
        await using var upstream = await FakeUpstream.StartAsync();

        var attempts = 0;
        upstream.RespondWith(async ctx =>
        {
            Interlocked.Increment(ref attempts);
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            ctx.Response.StatusCode = 503; // would trigger a retry, if POST were retryable
            await ctx.Response.WriteAsync("fail");
        });

        await using var factory = await GatewayFactory.CreateAsync(upstream, RetryRoutes(upstream.BaseUrl));
        using var client = factory.CreateClient();

        var body = Body(64 * 1024);
        await client.PostAsync("/api/data", new StringContent(body, System.Text.Encoding.ASCII, "text/plain"));

        Assert.Equal(1, attempts); // one attempt only — the 503 is surfaced, not retried
        Assert.Equal(body, Assert.Single(upstream.ReceivedRequests).Body); // and it arrived whole
    }
}

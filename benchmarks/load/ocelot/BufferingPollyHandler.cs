using Ocelot.Configuration;
using Ocelot.Provider.Polly.Interfaces;
using Polly;

namespace Ocelot.Bench;

/// <summary>
/// Makes Ocelot's hand-added retry actually able to retry a request with a body — using the fix
/// every source recommends for this exact error.
///
/// Without it, attempt 2 fails with "Sent 0 request content bytes, but Content-Length promised N":
/// HttpContent is single-use, and Ocelot maps the incoming body straight through, so the first
/// attempt drains the stream and leaves nothing to re-send. The same failure is a long-standing
/// YARP issue (microsoft/reverse-proxy#2125). The accepted answers are all one idea — make the
/// content re-readable before you retry — via either <c>LoadIntoBufferAsync()</c> or reading to a
/// byte array and rebuilding the content per attempt.
///
/// So this calls <c>LoadIntoBufferAsync()</c> once, outside the pipeline, before any attempt runs.
/// And that is the finding rather than a footnote: **the fix for "retry cannot replay the body" is
/// to buffer the body.** There is no third option — a replay needs the bytes, and a stream that has
/// been sent no longer has them.
///
/// What it costs is the whole point of the comparison. <c>LoadIntoBufferAsync()</c> buffers the
/// entire body into memory, per in-flight request, with no spill to disk and no gateway-wide
/// ceiling: 100 concurrent 10 MB uploads is 1 GB of heap and an OOM, and the only lever is the
/// optional maxBufferSize, which throws rather than degrades. That is precisely the naive shape
/// ConduitSharp's tiered budget replaces (RAM tier -> disk spill -> 503) and that APISIX replaces
/// with nginx's client_body_buffer_size plus temp-file spill.
/// </summary>
public sealed class BufferingPollyHandler : DelegatingHandler
{
    private readonly DownstreamRoute _route;
    private readonly IPollyQoSResiliencePipelineProvider<HttpResponseMessage> _provider;

    public BufferingPollyHandler(DownstreamRoute route, IPollyQoSResiliencePipelineProvider<HttpResponseMessage> provider)
    {
        _route = route;
        _provider = provider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Once, before the pipeline: every attempt inside it re-reads this buffer rather than the
        // consumed network stream. Unbounded by construction — the body is now heap, in full,
        // for as long as the request is in flight.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync();
        }

        var pipeline = _provider.GetResiliencePipeline(_route);
        return await pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token),
            cancellationToken);
    }
}

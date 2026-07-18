using Ocelot.Configuration;
using Ocelot.Provider.Polly.Interfaces;
using Polly;

namespace Ocelot.Bench;

/// <summary>
/// The same retry without the body buffering, kept so the failure is reproducible rather than
/// anecdotal: this is what a hand-added Ocelot retry does by default, and what
/// <see cref="BufferingPollyHandler"/> exists to fix.
///
/// Attempt 1 drains the stream; attempt 2 sends nothing and .NET rejects it with "Sent 0 request
/// content bytes, but Content-Length promised N", which Ocelot surfaces as a 502. Reachable with
/// OCELOT_RETRY=broken.
/// </summary>
public sealed class NonBufferingPollyHandler : DelegatingHandler
{
    private readonly DownstreamRoute _route;
    private readonly IPollyQoSResiliencePipelineProvider<HttpResponseMessage> _provider;

    public NonBufferingPollyHandler(DownstreamRoute route, IPollyQoSResiliencePipelineProvider<HttpResponseMessage> provider)
    {
        _route = route;
        _provider = provider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pipeline = _provider.GetResiliencePipeline(_route);
        return await pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token),
            cancellationToken);
    }
}

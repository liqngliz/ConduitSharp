using System.Net;
using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Memory a plugin holds on the *streaming* path is still memory. A capture plugin bounds itself per
/// request but multiplies by concurrency, and because it never touched the body buffer it used to be
/// invisible to <c>MaxTotalBufferedBodyBytes</c> — so a connection flood grew it unchecked while a
/// buffering plugin, whose bytes are budgeted, would already be shedding. Declaring
/// <see cref="IPipelinePlugin.CaptureMemoryBytes"/> puts it under the same ceiling and the same 503.
/// </summary>
[Trait("Category", "Security")]
public sealed class CaptureMemoryBudgetTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    /// <summary>
    /// Stands in for the body-capture tee: streams the body (never sets ReadsRequestBody) but
    /// declares the prefix it keeps, taking its size from the route's own config.
    /// </summary>
    private sealed class StreamingCapturePlugin : IPipelinePlugin
    {
        public PluginName Name => PluginName.Custom;
        public string Id => "custom-streaming-capture";
        public string? Variant => "streaming-capture";

        // Streaming path: the gateway must not buffer the body on this plugin's behalf.
        public bool ReadsRequestBody => false;

        public int CaptureMemoryBytes(JsonElement config) =>
            config.ValueKind == JsonValueKind.Object
            && config.TryGetProperty("maxSize", out var p)
            && p.TryGetInt32(out var v) ? v : 0;

        public Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next) => next(context);
    }

    /// <summary>
    /// Reads the request body and declares no capture of its own — its only job is to put the route
    /// on the buffered branch, where the capture reservation used to be skipped.
    /// </summary>
    private sealed class BodyReadingPlugin : IPipelinePlugin
    {
        public PluginName Name => PluginName.Custom;
        public string Id => "custom-body-reader";
        public string? Variant => "body-reader";

        public bool ReadsRequestBody => true;

        public Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next) => next(context);
    }

    private static string Routes(string upstreamBaseUrl, int maxSize) =>
        GatewayTestHelpers.RoutesWithPlugin(upstreamBaseUrl, "custom",
            ("streaming-capture", (object)new { maxSize }));

    /// <summary>
    /// The same capture plugin, but sharing the route with a body-reading plugin so the chain takes
    /// the buffered branch instead of the streaming one.
    /// </summary>
    private static string BufferedRoutes(string upstreamBaseUrl, int maxSize) => $$"""
        {
          "routes": [{
            "id": "buffered-capture-route",
            "route": { "match": { "path": "/{**rest}" } },
            "cluster": {
              "loadBalancingPolicy": "RoundRobin",
              "destinations": { "node-0": { "address": "{{upstreamBaseUrl}}" } },
              "httpRequest": { "activityTimeout": "00:00:05" }
            },
            "plugins": [
              { "name": "custom", "variant": "streaming-capture", "order": 1, "enabled": true,
                "config": { "maxSize": {{maxSize}} } },
              { "name": "custom", "variant": "body-reader", "order": 2, "enabled": true, "config": {} }
            ]
          }]
        }
        """;

    [Fact]
    public async Task CaptureMemory_CountsAgainstTotalBudget_ShedsWith503()
    {
        // Capture wants 32 KiB per request against a 4 KiB gateway-wide ceiling: the reservation
        // cannot fit, so the request sheds rather than quietly growing the process.
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes(_upstream.BaseUrl, 32 * 1024),
            plugins: [new StreamingCapturePlugin()],
            settings: new Dictionary<string, string?>
            {
                ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"] = "4096",
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/a/x", new StringContent("hello"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task CaptureMemory_OnTheBufferedPath_StillCountsAgainstTotalBudget_ShedsWith503()
    {
        // The reservation used to live inside the streaming branch only. Put the same capture plugin
        // on a route that also carries a body-reading plugin and the chain takes the buffered branch,
        // where the declaration was dropped on the floor — capture grew unbudgeted on exactly the
        // routes most likely to have it. Same 32 KiB want against the same 4 KiB ceiling: same 503.
        await using var factory = await GatewayFactory.CreateAsync(_upstream, BufferedRoutes(_upstream.BaseUrl, 32 * 1024),
            plugins: [new StreamingCapturePlugin(), new BodyReadingPlugin()],
            settings: new Dictionary<string, string?>
            {
                ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"] = "4096",
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/x", new StringContent("hello"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task CaptureMemory_WithinBudget_IsServed_AndReleasedForTheNextRequest()
    {
        // The other half, and the one a leak would break: the reservation must come back on
        // completion. Serially reusing headroom many times over proves Release runs — without it
        // the ceiling ratchets down and a later request 503s.
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes(_upstream.BaseUrl, 4096),
            plugins: [new StreamingCapturePlugin()],
            settings: new Dictionary<string, string?>
            {
                ["Gateway:RequestLimits:MaxTotalBufferedBodyBytes"] = "8192",
            });
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsync("/a/x", new StringContent($"body-{i}"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(10, _upstream.ReceivedRequests.Count);
    }
}

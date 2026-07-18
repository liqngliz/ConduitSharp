using BenchmarkDotNet.Attributes;
using ConduitSharp.Gateway.Routing;
using Microsoft.AspNetCore.Builder;
using Ocelot.Bench;
using Ocelot.Provider.Polly;

namespace ConduitSharp.Benchmarks;

/// <summary>
/// Upload path head-to-head: bodies through each gateway to the same loopback upstream.
/// Each gateway appears twice — default (streams: nothing consumes a buffer) and
/// retry (PUT on a retry route: the price of a rewindable body). The retry arms are
/// same-on-same RAM: ConduitSharp spills to tmpfs where the host has one (/dev/shm on
/// the Linux CI runner), Ocelot's hand-added retry holds the whole body on the heap
/// via LoadIntoBufferAsync (the load rig's BufferingPollyHandler, compiled in here).
/// Allocations per request are the deterministic column.
/// </summary>
[MemoryDiagnoser]
public class GatewayBodyComparisonBenchmarks
{
    [Params("ConduitSharp", "ConduitSharp-retry", "Ocelot", "Ocelot-retry")]
    public string Gateway = "";

    // 1 KB / 10 MB
    [Params(1, 10240)]
    public int BodyKB;

    private WebApplication _upstream = null!;
    private IAsyncDisposable _gateway = null!;
    private HttpClient _client = null!;
    private byte[] _body = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _body = new byte[BodyKB * 1024];
        Random.Shared.NextBytes(_body);

        string upstreamUrl;
        (_upstream, upstreamUrl) = await ComparisonRig.StartUpstreamAsync();

        if (Gateway.StartsWith("ConduitSharp", StringComparison.Ordinal))
        {
            var routes = new GatewayRoutesConfiguration();
            routes.Routes.Add(BenchGateway.Route(
                "up", "/{**catch-all}",
                withCluster: true,
                withRetry: Gateway == "ConduitSharp-retry",
                maxRequestBodyBytes: 64 * 1024 * 1024,
                upstreamAddress: upstreamUrl));
            // Same-on-same with Ocelot's heap buffer: spill to tmpfs where the host has one,
            // so neither retry arm pays disk. Locally (macOS: no /dev/shm) the temp dir stands in.
            var settings = Gateway == "ConduitSharp-retry" && Directory.Exists("/dev/shm")
                ? new Dictionary<string, string?> { ["Gateway:RequestLimits:SpillDirectory"] = "/dev/shm" }
                : null;
            var (app, client) = await BenchGateway.StartAsync(routes, settings: settings, realForwarder: true);
            (_gateway, _client) = (app, client);
        }
        else
        {
            var withRetry = Gateway == "Ocelot-retry";
            // QoSOptions is what makes Ocelot attach the QoS delegating handler at all —
            // without the block the AddPolly handler (and so the retry) never runs.
            // Values mirror the load rig's ocelot-retry.json.
            var route = ComparisonRig.OcelotRoute("/{everything}", upstreamUrl,
                method: withRetry ? "Put" : "Post",
                extraJson: withRetry
                    ? """
                      , "QoSOptions": { "ExceptionsAllowedBeforeBreaking": 0, "DurationOfBreak": 1000, "TimeoutValue": 30000 }
                      """
                    : "");
            var (app, client) = await ComparisonRig.StartOcelotAsync(
                ComparisonRig.OcelotConfig([route]),
                configureOcelot: withRetry
                    ? o => o.AddPolly<RetryQoSProvider>((r, _, _) => new BufferingPollyHandler(r, new RetryQoSProvider()))
                    : null);
            (_gateway, _client) = (app, client);
        }

        (await SendAsync()).EnsureSuccessStatusCode();
    }

    [Benchmark]
    public async Task<HttpResponseMessage> PostBody()
    {
        var response = await SendAsync();
        response.Dispose();
        return response;
    }

    private async Task<HttpResponseMessage> SendAsync()
    {
        using var content = new ByteArrayContent(_body);
        // Retry-buffered path only applies to idempotent methods — PUT there, POST elsewhere.
        return Gateway.EndsWith("-retry", StringComparison.Ordinal)
            ? await _client.PutAsync("/upload", content)
            : await _client.PostAsync("/upload", content);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _gateway.DisposeAsync();
        await _upstream.DisposeAsync();
    }
}

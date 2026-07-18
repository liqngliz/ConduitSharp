using BenchmarkDotNet.Attributes;
using ConduitSharp.Gateway.Routing;
using Microsoft.AspNetCore.Builder;

namespace ConduitSharp.Benchmarks;

/// <summary>
/// Body through the full proxy path (plugin dispatch → buffer/stream decision → YARP forward
/// to in-memory upstream). Three modes:
///   Auto       — plain route, POST: nothing consumes a buffer → streams automatically (the default path)
///   Buffered   — retry route, PUT (idempotent → rewindable): memory up to the threshold, temp-file spill above
///   StreamOnly — explicit streamOnly route, POST
/// B/op is the claim: Auto ≈ StreamOnly, and Buffered stays flat (threshold-capped heap) as the body grows.
/// </summary>
[MemoryDiagnoser]
public class BodyBenchmarks
{
    [Params("Auto", "Buffered", "StreamOnly")]
    public string Mode = "";

    // 1 KB / 1 MB / 10 MB
    [Params(1, 1024, 10240)]
    public int BodyKB;

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private byte[] _body = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _body = new byte[BodyKB * 1024];
        Random.Shared.NextBytes(_body);

        var routes = new GatewayRoutesConfiguration();
        routes.Routes.Add(BenchGateway.Route(
            "u", "/{**catch-all}",
            withCluster: true,
            streamOnly: Mode == "StreamOnly",
            withRetry: Mode == "Buffered",
            maxRequestBodyBytes: 64 * 1024 * 1024));

        (_app, _client) = await BenchGateway.StartAsync(routes);

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
        // PUT is the only method the buffered path applies to (POST never retries → streams).
        return Mode == "Buffered"
            ? await _client.PutAsync("/upload", content)
            : await _client.PostAsync("/upload", content);
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _app.DisposeAsync();
}

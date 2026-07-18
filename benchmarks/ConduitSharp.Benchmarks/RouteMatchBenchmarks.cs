using BenchmarkDotNet.Attributes;
using ConduitSharp.Gateway.Routing;
using Microsoft.AspNetCore.Builder;

namespace ConduitSharp.Benchmarks;

/// <summary>
/// End-to-end request against a gateway with N routes; the request hits the LAST
/// declared route (worst case for a linear matcher). Flat time across N proves
/// endpoint-routing DFA — rebuts the "O(N) RouteMatcher" critique.
/// Cluster-less routes + responder plugin: no YARP forward, no upstream in the number.
/// </summary>
[MemoryDiagnoser]
public class RouteMatchBenchmarks
{
    [Params(1, 10, 100, 500)]
    public int RouteCount;

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _lastRoutePath = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var routes = new GatewayRoutesConfiguration();
        for (var i = 0; i < RouteCount; i++)
            routes.Routes.Add(BenchGateway.Route(
                $"r{i}", $"/r{i}", plugins: BenchGateway.Custom("bench-responder", 1)));

        _lastRoutePath = $"/r{RouteCount - 1}";
        (_app, _client) = await BenchGateway.StartAsync(routes, [new ResponderPlugin()]);

        // sanity: route actually answers
        (await _client.GetAsync(_lastRoutePath)).EnsureSuccessStatusCode();
    }

    [Benchmark]
    public async Task<HttpResponseMessage> MatchLastRoute()
    {
        var response = await _client.GetAsync(_lastRoutePath);
        response.Dispose();
        return response;
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _app.DisposeAsync();
}

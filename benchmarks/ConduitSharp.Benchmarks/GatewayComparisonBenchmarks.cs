using BenchmarkDotNet.Attributes;
using ConduitSharp.Gateway.Routing;
using Microsoft.AspNetCore.Builder;

namespace ConduitSharp.Benchmarks;

/// <summary>
/// In-proc head-to-head: ConduitSharp vs Ocelot per-request cost as the route table
/// grows. Both gateways get RouteCount exact routes and every request hits the LAST
/// one — worst case for a linear matcher. ConduitSharp rides ASP.NET endpoint
/// routing's DFA (flat); Ocelot's route finder scans templates per request.
/// Both forward over a real loopback socket to the same 1 KB upstream.
/// </summary>
[MemoryDiagnoser]
public class GatewayComparisonBenchmarks
{
    [Params("ConduitSharp", "Ocelot")]
    public string Gateway = "";

    [Params(1, 100, 500)]
    public int RouteCount;

    private WebApplication _upstream = null!;
    private IAsyncDisposable _gateway = null!;
    private HttpClient _client = null!;
    private string _lastRoute = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        string upstreamUrl;
        (_upstream, upstreamUrl) = await ComparisonRig.StartUpstreamAsync();
        _lastRoute = $"/r{RouteCount - 1}";

        if (Gateway == "ConduitSharp")
        {
            var routes = new GatewayRoutesConfiguration();
            for (var i = 0; i < RouteCount; i++)
                routes.Routes.Add(BenchGateway.Route(
                    $"r{i}", $"/r{i}", withCluster: true, upstreamAddress: upstreamUrl));
            var (app, client) = await BenchGateway.StartAsync(routes, realForwarder: true);
            (_gateway, _client) = (app, client);
        }
        else
        {
            var routeBlocks = Enumerable.Range(0, RouteCount)
                .Select(i => ComparisonRig.OcelotRoute($"/r{i}", upstreamUrl));
            var (app, client) = await ComparisonRig.StartOcelotAsync(ComparisonRig.OcelotConfig(routeBlocks));
            (_gateway, _client) = (app, client);
        }

        (await _client.GetAsync(_lastRoute)).EnsureSuccessStatusCode();
    }

    [Benchmark]
    public async Task<HttpResponseMessage> ProxiedGet()
    {
        var response = await _client.GetAsync(_lastRoute);
        response.Dispose();
        return response;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _gateway.DisposeAsync();
        await _upstream.DisposeAsync();
    }
}

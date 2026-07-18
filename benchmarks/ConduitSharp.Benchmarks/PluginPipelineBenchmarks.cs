using BenchmarkDotNet.Attributes;
using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway.Routing;
using Microsoft.AspNetCore.Builder;

namespace ConduitSharp.Benchmarks;

/// <summary>
/// One route, N no-op plugins ahead of a responder terminal. Time delta between
/// N=0/1/5 is the per-plugin dispatch overhead — should be ~constant per plugin.
/// </summary>
[MemoryDiagnoser]
public class PluginPipelineBenchmarks
{
    [Params(0, 1, 5)]
    public int NoopPlugins;

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var plugins = new List<PluginConfig>();
        for (var i = 0; i < NoopPlugins; i++)
            plugins.Add(BenchGateway.Custom("bench-noop", i + 1));
        plugins.Add(BenchGateway.Custom("bench-responder", NoopPlugins + 1));

        var routes = new GatewayRoutesConfiguration();
        routes.Routes.Add(BenchGateway.Route("p", "/p", plugins: [.. plugins]));

        (_app, _client) = await BenchGateway.StartAsync(
            routes, [new NoopPlugin(), new ResponderPlugin()]);

        (await _client.GetAsync("/p")).EnsureSuccessStatusCode();
    }

    [Benchmark]
    public async Task<HttpResponseMessage> Request()
    {
        var response = await _client.GetAsync("/p");
        response.Dispose();
        return response;
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _app.DisposeAsync();
}

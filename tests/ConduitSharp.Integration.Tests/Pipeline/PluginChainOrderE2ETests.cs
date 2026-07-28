using System.Collections.Concurrent;
using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace ConduitSharp.Integration.Tests.Pipeline;

/// <summary>
/// Proves the plugin chain runs down on the request and up in reverse on the response, end to end
/// through the real gateway over HTTP, and that a cache hit short-circuits at the cache so the
/// forward is never reached. The order is read from actual log records the probe plugins emit through
/// the host's ILoggerFactory, captured in emission order, not from an in-memory list.
///
/// Route models: probe-a(1), probe-b(2), probe-c(3), cache(4), probe-e(5), then the YARP forward.
/// probe-e stands in front of the forward, so its presence in the log means the request reached the
/// forward stage; its absence means the cache stopped the request first.
/// </summary>
public sealed class PluginChainOrderE2ETests : IAsyncLifetime
{
    private readonly ITestOutputHelper _out;
    private FakeUpstream _upstream = null!;

    public PluginChainOrderE2ETests(ITestOutputHelper output) => _out = output;
    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    private static string ChainRoutes(string upstream) => $$"""
    {
      "routes": [{
        "id": "chain",
        "route": { "match": { "path": "/{**rest}" } },
        "cluster": {
          "loadBalancingPolicy": "RoundRobin",
          "destinations": { "node-0": { "address": "{{upstream}}" } },
          "httpRequest": { "activityTimeout": "00:00:05" }
        },
        "plugins": [
          { "name": "custom", "variant": "probe-a", "order": 1 },
          { "name": "custom", "variant": "probe-b", "order": 2 },
          { "name": "custom", "variant": "probe-c", "order": 3 },
          { "name": "cache",  "order": 4, "config": { "ttlSeconds": 60, "varyByHeaders": [], "maxCacheableBytes": 1048576 } },
          { "name": "custom", "variant": "probe-e", "order": 5 }
        ]
      }]
    }
    """;

    [Fact]
    public async Task Request_runs_down_response_runs_up_and_cache_hit_stops_at_cache()
    {
        var log = new SequenceLog();
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream,
            ChainRoutes(_upstream.BaseUrl),
            plugins: [new OrderProbe("a"), new OrderProbe("b"), new OrderProbe("c"), new OrderProbe("e")],
            configureWebHost: b => b.ConfigureLogging(lb =>
            {
                lb.SetMinimumLevel(LogLevel.Information);
                lb.AddProvider(log);
            }));
        using var client = factory.CreateClient();

        // Request 1: cache miss. Down 1,2,3,(cache),5 then up 5,(cache),3,2,1. Forward is reached.
        log.Events.Clear();
        var r1 = await client.GetAsync("/data");
        var seq1 = log.Events.ToArray();
        _out.WriteLine("req1 (miss): " + string.Join(" ", seq1));
        Assert.Equal(System.Net.HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(
            new[] { "a:enter", "b:enter", "c:enter", "e:enter", "e:exit", "c:exit", "b:exit", "a:exit" },
            seq1);
        Assert.Single(_upstream.ReceivedRequests); // forward ran once

        // Request 2: same path, cache HIT. Down 1,2,3 then the cache answers and unwinds 3,2,1.
        // probe-e never runs and the forward is never reached.
        log.Events.Clear();
        var r2 = await client.GetAsync("/data");
        var seq2 = log.Events.ToArray();
        _out.WriteLine("req2 (hit):  " + string.Join(" ", seq2));
        Assert.Equal(System.Net.HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("HIT", r2.Headers.GetValues("X-Cache").Single());
        Assert.Equal(
            new[] { "a:enter", "b:enter", "c:enter", "c:exit", "b:exit", "a:exit" },
            seq2);
        Assert.DoesNotContain("e:enter", seq2);
        Assert.Single(_upstream.ReceivedRequests); // still 1 — forward NOT reached on the hit

        // Request 3: different path, cache miss again. Full chain, forward reached a second time.
        log.Events.Clear();
        var r3 = await client.GetAsync("/other");
        var seq3 = log.Events.ToArray();
        _out.WriteLine("req3 (miss): " + string.Join(" ", seq3));
        Assert.Equal(System.Net.HttpStatusCode.OK, r3.StatusCode);
        Assert.Equal(
            new[] { "a:enter", "b:enter", "c:enter", "e:enter", "e:exit", "c:exit", "b:exit", "a:exit" },
            seq3);
        Assert.Equal(2, _upstream.ReceivedRequests.Count);
    }

    // A plugin that logs "enter" before next and "exit" after, through the host's real ILoggerFactory
    // under category "Probe.{id}". Nothing is recorded in-plugin; the order comes from the log sink.
    private sealed class OrderProbe(string id) : IPipelinePlugin
    {
        public PluginName Name => PluginName.Custom;
        public string? Variant => $"probe-{id}";
        public string Id => $"probe-{id}";

        public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
        {
            var log = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger($"Probe.{id}");
            log.LogInformation("enter");
            await next(context);
            log.LogInformation("exit");
        }
    }

    // Records probe log messages in emission order as "{id}:{message}", e.g. "a:enter".
    private sealed class SequenceLog : ILoggerProvider
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, Events);
        public void Dispose() { }

        private sealed class Recorder(string category, ConcurrentQueue<string> events) : ILogger
        {
            private const string Prefix = "Probe.";
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
            {
                if (category.StartsWith(Prefix, StringComparison.Ordinal))
                    events.Enqueue($"{category[Prefix.Length..]}:{formatter(state, ex)}");
            }
        }
    }
}

using System.Collections.Concurrent;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// IRequestObserver fan-out. Regression: the observers (structured request log, OTel request
/// metrics) were registered in DI but the YARP re-platforming deleted the middleware that
/// notified them — every request went unobserved and nothing failed. These tests fail if the
/// wiring is ever dropped again.
/// </summary>
public sealed class RequestObserverTests : IAsyncLifetime
{
    private sealed class RecordingObserver : IRequestObserver
    {
        public ConcurrentQueue<RequestObservation> Seen { get; } = new();
        public void OnRequestCompleted(RequestObservation observation) => Seen.Enqueue(observation);
    }

    private sealed class ThrowingObserver : IRequestObserver
    {
        public void OnRequestCompleted(RequestObservation observation) => throw new InvalidOperationException("boom");
    }

    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    private static Action<IWebHostBuilder> With(params IRequestObserver[] observers) => builder =>
        builder.ConfigureServices(services =>
        {
            foreach (var observer in observers)
                services.AddSingleton(observer);
        });

    [Fact]
    public async Task ForwardedRequest_IsObserved_WithRouteIdStatusAndTiming()
    {
        var observer = new RecordingObserver();
        _upstream.RespondWith(200, "ok");
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, configureWebHost: With(observer));
        using var client = factory.CreateClient();

        await client.GetAsync("/api/data");

        var seen = Assert.Single(observer.Seen);
        Assert.Equal("GET", seen.Method);
        Assert.Equal("/api/data", seen.Path);
        Assert.Equal("test-passthrough", seen.RouteId);
        Assert.Equal(200, seen.StatusCode);
        Assert.True(seen.DurationMs >= 0);
        Assert.False(string.IsNullOrEmpty(seen.RequestId));
    }

    [Fact]
    public async Task UnmatchedRequest_IsObservedToo_WithNullRouteId()
    {
        var observer = new RecordingObserver();
        var routes = $$"""
            {
              "routes": [{
                "id": "narrow",
                "route": { "match": { "path": "/only-this" } },
                "cluster": {
                  "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } }
                },
                "plugins": []
              }]
            }
            """;
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, routes, configureWebHost: With(observer));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/somewhere-else");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        var seen = Assert.Single(observer.Seen);
        Assert.Null(seen.RouteId);      // no route matched — still observed
        Assert.Equal(404, seen.StatusCode);
    }

    [Fact]
    public async Task ThrowingObserver_NeverFailsTheRequest_AndOthersStillRun()
    {
        var recording = new RecordingObserver();
        _upstream.RespondWith(200, "ok");
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, configureWebHost: With(new ThrowingObserver(), recording));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Single(recording.Seen);
    }
}

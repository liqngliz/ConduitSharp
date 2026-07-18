using System.Diagnostics;
using System.Net;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Observability.Telemetry;
using Xunit;

namespace ConduitSharp.Integration.Tests.Gateway;

public sealed class TracingTests
{
    // -------------------------------------------------------------------------
    // Register an ActivityListener so GatewayTelemetry.ActivitySource.StartActivity()
    // returns a non-null Activity. This covers the non-null branches of every
    // activity?.SetTag(...) call in GatewayMiddleware.InvokeAsync.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Request_WithActivityListener_SpanIsCreatedAndTagged()
    {
        var spans = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo   = source => source.Name == GatewayTelemetry.SourceName,
            Sample           = (ref ActivityCreationOptions<ActivityContext> _)
                                   => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted  = _ => { },
            ActivityStopped  = a => spans.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "traced");

        await using var factory = await GatewayFactory.CreateAsync(upstream);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/traced-path");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var span = Assert.Single(spans, s => s.OperationName == "gateway.request");
        Assert.Equal("GET",          span.GetTagItem("http.request.method"));
        Assert.Equal("/traced-path", span.GetTagItem("url.path"));
        Assert.Equal("test-passthrough", span.GetTagItem("conduitsharp.route_id"));
        Assert.Equal(200,            span.GetTagItem("http.response.status_code"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task Request_5xxUpstream_SpanMarkedAsError()
    {
        var spans = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo  = source => source.Name == GatewayTelemetry.SourceName,
            Sample          = (ref ActivityCreationOptions<ActivityContext> _)
                                  => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = a => spans.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(500, "server error");

        await using var factory = await GatewayFactory.CreateAsync(upstream);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/fail");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var span = Assert.Single(spans, s => s.OperationName == "gateway.request");
        Assert.Equal(500, span.GetTagItem("http.response.status_code"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task Request_NoMatchingRoute_SpanHasNoRouteId()
    {
        var spans = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo  = source => source.Name == GatewayTelemetry.SourceName,
            Sample          = (ref ActivityCreationOptions<ActivityContext> _)
                                  => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = a => spans.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        // Route only matches /specific — /other will return 404
        await using var upstream = await FakeUpstream.StartAsync();
        var routes = $$"""
            {
              "routes": [{
                "id": "specific-route",
                "route": { "match": { "path": "/specific" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" }
                },
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(upstream, routes);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/other");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var span = Assert.Single(spans, s => s.OperationName == "gateway.request");
        Assert.Null(span.GetTagItem("conduitsharp.route_id"));
    }

    [Fact]
    public async Task Forwarding_ProducesForwardSpan()
    {
        // The forward to the upstream is traced whether or not "http-proxy" is named in the
        // plugin list — implicit and explicit forwarding produce the same trace shape.
        var spans = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo  = source => source.Name == PipelineTelemetry.SourceName,
            Sample          = (ref ActivityCreationOptions<ActivityContext> _)
                                  => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = a => spans.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream); // default route, no plugins
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/anything");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var span = Assert.Single(spans, s => s.OperationName == "gateway.forward");
        Assert.Equal("test-passthrough", span.GetTagItem("conduitsharp.route_id"));
        Assert.Equal(1, span.GetTagItem("conduitsharp.attempt"));
    }
}

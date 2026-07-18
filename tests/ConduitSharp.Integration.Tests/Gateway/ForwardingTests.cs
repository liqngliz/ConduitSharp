using System.Text;
using ConduitSharp.Integration.Tests.Fixtures;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Verifies that matched requests are forwarded correctly: method, body, and
/// status codes are all preserved end-to-end.
/// </summary>
public sealed class ForwardingTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;
    private GatewayFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _upstream = await FakeUpstream.StartAsync();
        _factory  = await GatewayFactory.CreateAsync(_upstream);
        _client   = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _upstream.DisposeAsync();
    }

    [Fact]
    public async Task Get_MatchedRoute_ForwardsToUpstream()
    {
        var response = await _client.GetAsync("/api/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task Post_WithBody_ForwardsBodyAndMethodToUpstream()
    {
        var payload = """{"userId":42}""";
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/data", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var req = Assert.Single(_upstream.ReceivedRequests);
        Assert.Equal("POST", req.Method);
        Assert.Equal(payload, req.Body);
    }

    [Fact]
    public async Task Upstream_NonSuccessStatus_IsForwardedToClient()
    {
        _upstream.RespondWith(503);

        var response = await _client.GetAsync("/api/anything");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task Post_WithBody_NoContentType_ForwardsBodyWithoutContentTypeHeader()
    {
        // A POST with a body but no Content-Type header exercises the
        // no-Content-Type branch in GatewayMiddlewareExt.
        var content = new ByteArrayContent("raw body bytes"u8.ToArray());

        var response = await _client.PostAsync("/api/data", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var req = Assert.Single(_upstream.ReceivedRequests);
        Assert.Equal("POST", req.Method);
        Assert.Equal("raw body bytes", req.Body);
        Assert.False(req.Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public async Task Post_WithEntityHeaders_ForwardsAllContentHeadersToUpstream()
    {
        // Regression: only Content-Type used to be copied onto the outgoing content —
        // Content-Encoding, Content-Language, etc. were silently dropped.
        var content = new ByteArrayContent("pretend-gzipped"u8.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
        content.Headers.TryAddWithoutValidation("Content-Language", "en-US");

        var response = await _client.PostAsync("/api/data", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var req = Assert.Single(_upstream.ReceivedRequests);
        Assert.Equal("application/json", req.Headers["Content-Type"]);
        Assert.Equal("gzip",             req.Headers["Content-Encoding"]);
        Assert.Equal("en-US",            req.Headers["Content-Language"]);
    }

    [Fact]
    public async Task Upstream_HopByHopResponseHeaders_AreNotRelayedToClient()
    {
        // Hop-by-hop headers describe the gateway↔upstream connection; the request side
        // already stripped them and the response side must do the same.
        _upstream.RespondWith(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["Proxy-Authenticate"] = "Basic realm=\"upstream\"";
            ctx.Response.Headers["X-Upstream-App"]     = "kept";
            await ctx.Response.WriteAsync("ok");
        });

        var response = await _client.GetAsync("/api/anything");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Proxy-Authenticate"));
        Assert.Equal("kept", response.Headers.GetValues("X-Upstream-App").Single());
    }
}

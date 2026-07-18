using System.Diagnostics;
using System.Text;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;

namespace ConduitSharp.Integration.Tests.Pipeline.Security;

/// <summary>
/// Tests the bearer extraction and token structure checks that fire before any
/// JWKS network call, so no live key server is needed.
/// </summary>
public sealed class JwksJwtAuthEndToEndTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync() => await _upstream.DisposeAsync();

    private string Routes() =>
        GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "jwks-jwt-auth",
            new { jwksUri = $"{_upstream.BaseUrl}/jwks" });

    [Fact]
    public async Task NoAuthHeader_Returns401()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task MalformedToken_TwoSegments_Returns401()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream, Routes());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer only.two");

        var response = await client.GetAsync("/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task SlowJwks_ReturnsErrorWithinTimeout()
    {
        // The JWKS endpoint stalls for 5 s; the route allows only 500 ms to fetch it.
        // A well-formed RS256 token reaches the fetch step, so the timeout — not the
        // full upstream delay — must decide the outcome: 401, returned promptly.
        const int timeoutMs = 500;
        const int upstreamLag = 5_000;

        _upstream.RespondWith(async ctx =>
        {
            await Task.Delay(upstreamLag, ctx.RequestAborted);
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("""{"keys":[]}""");
        });

        var routes = GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "jwks-jwt-auth",
            new { jwksUri = $"{_upstream.BaseUrl}/jwks", jwksTimeoutMs = timeoutMs });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {WellFormedRs256Token()}");

        var sw = Stopwatch.StartNew();
        var response = await client.GetAsync("/protected");
        sw.Stop();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(sw.ElapsedMilliseconds < upstreamLag,
            $"Auth took {sw.ElapsedMilliseconds} ms — the {timeoutMs} ms JWKS timeout did not fire.");
    }

    // Structurally valid RS256 token (real header + payload, dummy signature). Enough to
    // pass parsing and the algorithm check so the handler proceeds to the JWKS fetch.
    private static string WellFormedRs256Token()
    {
        static string B64Url(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64Url("""{"alg":"RS256","typ":"JWT","kid":"k1"}""");
        var payload = B64Url("""{"sub":"demo"}""");
        return $"{header}.{payload}.AAAA";
    }
    [Fact]
    public async Task SlowJwks_FirstRequestTimesOut_BackgroundFetchCompletes_SecondRequestHitsCache()
    {
        const int upstreamLag = 1000;
        const int timeoutMs = 500;

        _upstream.RespondWith(async ctx =>
        {
            if (ctx.Request.Path == "/jwks")
            {
                await Task.Delay(upstreamLag);
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync("{\"keys\":[]}");
            }
        });

        var routes = GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "jwks-jwt-auth",
            new { jwksUri = $"{_upstream.BaseUrl}/jwks", jwksTimeoutMs = timeoutMs, cacheTtlSeconds = 305 });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {WellFormedRs256Token()}");

        // First request: hits the 500ms timeout circuit breaker, returns 401
        var sw1 = Stopwatch.StartNew();
        var response1 = await client.GetAsync("/protected");
        sw1.Stop();

        Assert.Equal(HttpStatusCode.Unauthorized, response1.StatusCode);
        Assert.True(sw1.ElapsedMilliseconds >= timeoutMs - 50 && sw1.ElapsedMilliseconds < upstreamLag, $"First request took {sw1.ElapsedMilliseconds}ms (expected around {timeoutMs}ms). Error: " + await response1.Content.ReadAsStringAsync());

        // Wait enough time for the background fetch to finish the 1000ms delay and populate the cache
        await Task.Delay(1500);

        // Second request: background fetch is already done, cache is populated.
        // It will instantly see the empty keys array and return 401 without any network delay!
        var sw2 = Stopwatch.StartNew();
        var response2 = await client.GetAsync("/protected");
        sw2.Stop();

        Assert.Equal(HttpStatusCode.Unauthorized, response2.StatusCode);
        Assert.True(sw2.ElapsedMilliseconds < 25,
            $"Second request took {sw2.ElapsedMilliseconds}ms (expected < 50ms because of cache hit)");
    }
}

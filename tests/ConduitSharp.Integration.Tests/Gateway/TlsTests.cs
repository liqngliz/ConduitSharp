using System.Net;
using ConduitSharp.Integration.Tests.Fixtures;
using Xunit;

namespace ConduitSharp.Integration.Tests.Gateway;

public sealed class TlsTests
{
    // -------------------------------------------------------------------------
    // skipCertificateVerification selects the "upstream-insecure" HttpClient.
    // The fake upstream is plain HTTP so the request succeeds regardless —
    // this test covers the branch in GatewayMiddleware that selects the client.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Request_SkipCertificateVerificationTrue_RoutesSuccessfully()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "ok");

        var routes = $$"""
            {
              "routes": [{
                "id": "tls-test",
                "route": { "match": { "path": "/{**rest}" } },
                "cluster": {
                  "loadBalancingPolicy": "RoundRobin",
                  "destinations": { "node-0": { "address": "{{upstream.BaseUrl}}" } },
                  "httpRequest": { "activityTimeout": "00:00:05" },
                  "httpClient": { "dangerousAcceptAnyServerCertificate": true }
                },
                "plugins": []
              }]
            }
            """;

        await using var factory = await GatewayFactory.CreateAsync(upstream, routes);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Request_SkipCertificateVerificationFalse_RoutesSuccessfully()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "ok");

        var routes = $$"""
            {
              "routes": [{
                "id": "tls-test",
                "route": { "match": { "path": "/{**rest}" } },
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

        var response = await client.GetAsync("/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

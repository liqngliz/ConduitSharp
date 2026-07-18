using System.Text.Json;
using BenchmarkDotNet.Attributes;
using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway.Routing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ConduitSharp.Benchmarks;

/// <summary>
/// Policy-chain head-to-head: JWT auth (HS256, same key) + rate limiting on both
/// gateways, GET through to the same loopback upstream. Mirrors the load rig's
/// scenario-b. Rate limits set far above achievable throughput so nothing throttles —
/// this measures the per-request cost of enforcing policy, not the throttle itself.
/// </summary>
[MemoryDiagnoser]
public class GatewayPolicyComparisonBenchmarks
{
    [Params("ConduitSharp", "Ocelot")]
    public string Gateway = "";

    private WebApplication _upstream = null!;
    private IAsyncDisposable _gateway = null!;
    private HttpClient _client = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        string upstreamUrl;
        (_upstream, upstreamUrl) = await ComparisonRig.StartUpstreamAsync();

        if (Gateway == "ConduitSharp")
        {
            var routes = new GatewayRoutesConfiguration();
            var route = BenchGateway.Route("policy", "/{**catch-all}", withCluster: true, upstreamAddress: upstreamUrl);
            route.Plugins.Add(new PluginConfig
            {
                Name  = PluginName.JwtAuth,
                Order = 1,
                Config = JsonSerializer.SerializeToElement(new
                {
                    signingKey = Convert.ToBase64String(ComparisonRig.JwtSecret),
                    algorithm  = "HS256",
                }),
            });
            route.Plugins.Add(new PluginConfig
            {
                Name  = PluginName.RateLimit,
                Order = 2,
                Config = JsonSerializer.SerializeToElement(new
                {
                    windowSeconds = 60,
                    maxRequests   = 1_000_000_000L,
                }),
            });
            routes.Routes.Add(route);
            var (app, client) = await BenchGateway.StartAsync(routes, realForwarder: true);
            (_gateway, _client) = (app, client);
        }
        else
        {
            var route = ComparisonRig.OcelotRoute("/{everything}", upstreamUrl, extraJson: """
                ,
                "AuthenticationOptions": { "AuthenticationProviderKey": "BenchKey" },
                "RateLimitOptions": {
                  "EnableRateLimiting": true,
                  "Period": "60s",
                  "PeriodTimespan": 60,
                  "Limit": 1000000000
                }
                """);
            var (app, client) = await ComparisonRig.StartOcelotAsync(
                ComparisonRig.OcelotConfig([route]),
                configure: builder => builder.Services
                    .AddAuthentication()
                    .AddJwtBearer("BenchKey", options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            IssuerSigningKey = new SymmetricSecurityKey(ComparisonRig.JwtSecret),
                            ValidateIssuer   = false,
                            ValidateAudience = false,
                        };
                    }));
            (_gateway, _client) = (app, client);
        }

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ComparisonRig.SignHs256Token());

        var probe = await _client.GetAsync("/bench");
        if (!probe.IsSuccessStatusCode)
            throw new InvalidOperationException($"{Gateway} policy setup broken: {(int)probe.StatusCode}");
    }

    [Benchmark]
    public async Task<HttpResponseMessage> AuthedGet()
    {
        var response = await _client.GetAsync("/bench");
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

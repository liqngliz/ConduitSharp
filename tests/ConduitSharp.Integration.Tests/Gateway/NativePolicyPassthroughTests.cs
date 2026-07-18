using System.Security.Claims;
using System.Text.Encodings.Web;
using ConduitSharp.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// ASP.NET Core's native per-route policy model works *through* routes.json: the route block is
/// YARP's RouteConfig verbatim, so <c>authorizationPolicy</c> flows to YARP untouched and the
/// host's registered policies + auto-inserted auth middleware enforce it. Pins the passthrough —
/// a translator change that dropped these fields would fail here, and the docs' claim that
/// "native policies and plugins compose" stays honest.
/// </summary>
public sealed class NativePolicyPassthroughTests : IAsyncLifetime
{
    // Minimal scheme: authenticates only when X-Test-User is present, with that name as a claim.
    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    private string Routes() => $$"""
        {
          "routes": [
            {
              "id": "protected",
              "route": {
                "match": { "path": "/secure/{**rest}" },
                "authorizationPolicy": "authenticated-users"
              },
              "cluster": { "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } } },
              "plugins": []
            },
            {
              "id": "open",
              "route": { "match": { "path": "/open/{**rest}" } },
              "cluster": { "destinations": { "node-0": { "address": "{{_upstream.BaseUrl}}" } } },
              "plugins": []
            }
          ]
        }
        """;

    private static void AddHostPolicies(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication("TestHeader")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("TestHeader", null);
            services.AddAuthorization(options =>
                options.AddPolicy("authenticated-users", p => p.RequireAuthenticatedUser()));
        });

    [Fact]
    public async Task RouteWithAuthorizationPolicy_ChallengesAnonymous_AndAdmitsAuthenticated()
    {
        _upstream.RespondWith(200, "ok");
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, Routes(), configureWebHost: AddHostPolicies);
        using var client = factory.CreateClient();

        var anonymous = await client.GetAsync("/secure/data");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(_upstream.ReceivedRequests);   // rejected before the forward

        var request = new HttpRequestMessage(HttpMethod.Get, "/secure/data");
        request.Headers.Add("X-Test-User", "alice");
        var authenticated = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Single(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task RouteWithoutPolicy_IsUntouchedByHostAuth()
    {
        _upstream.RespondWith(200, "ok");
        await using var factory = await GatewayFactory.CreateAsync(
            _upstream, Routes(), configureWebHost: AddHostPolicies);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/open/data");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}

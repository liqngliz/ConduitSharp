using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Locks in the documented plugin resolution order (ResolvePlugin is LastOrDefault):
/// built-in &lt; plugins-folder DLL &lt; host DI after AddConduitSharpGateway.
/// A refactor to FirstOrDefault, or reordering registrations, fails these tests.
/// </summary>
[Trait("Contract", "PluginIsolation")]
public sealed class PluginResolutionPrecedenceTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    /// <summary>Terminal plugin stamping a response header — makes the winning registration observable.</summary>
    private sealed class StampPlugin(PluginName name, string? variant, string stamp) : IPipelinePlugin
    {
        public PluginName Name => name;
        public string Id => $"stamp-{stamp}";
        public string? Variant => variant;

        public Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
        {
            context.Response.Headers["X-Stamp"] = stamp;
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HostDiRegistration_ReplacesBuiltInOfSameName()
    {
        // Route names the built-in api-key-auth with a valid config; the DI-registered
        // plugin with the same PluginName must serve instead. The built-in would 401
        // this key-less request — the stamp proves the override took the route.
        var routes = GatewayTestHelpers.RouteWithPlugin(_upstream.BaseUrl, "api-key-auth",
            new { header = "X-Api-Key", keys = new[] { "any-key" } });

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes,
            plugins: [new StampPlugin(PluginName.ApiKeyAuth, variant: null, stamp: "di-override")]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/data"); // no X-Api-Key header on purpose

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("di-override", response.Headers.GetValues("X-Stamp").Single());
        Assert.Empty(_upstream.ReceivedRequests);
    }

    [Fact]
    public async Task TwoCustomPluginsWithSameVariant_LastRegistrationWins()
    {
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "custom",
            (("dup", (object)new { })));

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes,
            plugins:
            [
                new StampPlugin(PluginName.Custom, variant: "dup", stamp: "first"),
                new StampPlugin(PluginName.Custom, variant: "dup", stamp: "second"),
            ]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/a/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("second", response.Headers.GetValues("X-Stamp").Single());
    }
}

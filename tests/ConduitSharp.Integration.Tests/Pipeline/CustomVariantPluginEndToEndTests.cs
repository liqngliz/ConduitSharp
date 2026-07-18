using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Integration.Tests.Pipeline;

/// <summary>
/// Per-route config isolation for custom-variant plugins — the same widening matrix the
/// built-in plugins get, exercised through the <c>"name": "custom", "variant": "…"</c> path.
/// </summary>
[Trait("Contract", "PluginIsolation")]
public sealed class CustomVariantPluginEndToEndTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    /// <summary>Key-auth clone registered under <see cref="PluginName.Custom"/> + a variant.</summary>
    private sealed class CustomKeyAuthPlugin(string variant) : IPipelinePlugin
    {
        public PluginName Name => PluginName.Custom;
        public string Id => $"custom-{variant}";
        public string? Variant => variant;

        public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
        {
            var keys = config.GetProperty("keys").EnumerateArray()
                .Select(k => k.GetString()!).ToHashSet();
            if (!context.Request.Headers.TryGetValue("X-Api-Key", out var supplied) ||
                !keys.Contains(supplied.ToString()))
            {
                context.Response.StatusCode = 401;
                return;
            }
            await next(context);
        }
    }

    [Fact]
    public async Task Same_variant_on_four_routes_keeps_separate_configs()
    {
        // One singleton plugin instance serves all four routes — same widening matrix
        // as the built-in api-key-auth test.
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "custom",
            ("key-auth", (object)new { keys = new[] { "key-a" } }),
            ("key-auth", new { keys = new[] { "key-b" } }),
            ("key-auth", new { keys = new[] { "key-c", "key-a" } }),
            ("key-auth", new { keys = new[] { "key-a", "key-b", "key-c" } }));

        var accepts = new Dictionary<char, string[]>
        {
            ['a'] = ["key-a"],
            ['b'] = ["key-b"],
            ['c'] = ["key-c", "key-a"],
            ['d'] = ["key-a", "key-b", "key-c"],
        };

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes,
            plugins: [new CustomKeyAuthPlugin("key-auth")]);
        using var client = factory.CreateClient();

        foreach (var route in "abcd")
        foreach (var key in new[] { "key-a", "key-b", "key-c", "key-d" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{route}/data");
            request.Headers.Add("X-Api-Key", key);

            var response = await client.SendAsync(request);

            var expected = accepts[route].Contains(key) ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
            Assert.True(expected == response.StatusCode,
                $"route /{route} with {key}: expected {expected}, got {response.StatusCode}");
        }
    }

    [Fact]
    public async Task Two_variants_resolve_independently_per_route()
    {
        // Both plugins share PluginName.Custom — routes must bind by variant, and each
        // variant must see only its own route's config.
        var routes = GatewayTestHelpers.RoutesWithPlugin(_upstream.BaseUrl, "custom",
            ("key-auth-one", (object)new { keys = new[] { "key-a" } }),
            ("key-auth-two", new { keys = new[] { "key-b" } }));

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes,
            plugins: [new CustomKeyAuthPlugin("key-auth-one"), new CustomKeyAuthPlugin("key-auth-two")]);
        using var client = factory.CreateClient();

        foreach (var (route, key, expected) in new[]
        {
            ('a', "key-a", HttpStatusCode.OK),
            ('a', "key-b", HttpStatusCode.Unauthorized),
            ('b', "key-b", HttpStatusCode.OK),
            ('b', "key-a", HttpStatusCode.Unauthorized),
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{route}/data");
            request.Headers.Add("X-Api-Key", key);

            var response = await client.SendAsync(request);

            Assert.True(expected == response.StatusCode,
                $"route /{route} with {key}: expected {expected}, got {response.StatusCode}");
        }
    }
}

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway;
using ConduitSharp.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ConduitSharp.Gateway.Routing;
using Yarp.ReverseProxy.Configuration;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Exercises the embeddable-library surface (AddConduitSharpGateway / UseConduitSharpGateway)
/// with non-default <see cref="ConduitSharpGatewayOptions"/>, which the standalone
/// <c>Program</c> path never hits: in-memory routes, a path prefix, and the composition
/// toggles for observability, plugin-folder scanning, admin, and health.
/// </summary>
public sealed class EmbeddedGatewayTests
{
    private static async Task<WebApplication> StartEmbeddedAsync(
        Action<ConduitSharpGatewayOptions> configure,
        IDictionary<string, string?>? settings = null,
        Action<WebApplication>? configureApp = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (settings is not null)
            builder.Configuration.AddInMemoryCollection(settings);

        builder.AddConduitSharpGateway(configure);

        var app = builder.Build();
        configureApp?.Invoke(app);
        app.UseConduitSharpGateway();
        await app.StartAsync();
        return app;
    }

    private static GatewayRoutesConfiguration CatchAll(string upstreamBaseUrl, string path = "/{**catch-all}") => new()
    {
        Routes =
        {
            new GatewayRoute
            {
                Id      = "embedded-test",
                Route   = new RouteConfig { Match = new RouteMatch { Path = path } },
                Cluster = new ClusterConfig
                {
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["node-0"] = new() { Address = upstreamBaseUrl },
                    },
                },
            },
        },
    };

    private static void Minimal(ConduitSharpGatewayOptions o, string upstreamBaseUrl, string routePath = "/{**catch-all}")
    {
        o.Routes                    = CatchAll(upstreamBaseUrl, routePath);
        o.ConfigureObservability    = false;
        o.EnablePluginDirectoryScan = false;
        o.EnableAdminApi            = false;
        o.MapHealthEndpoints        = false;
    }

    [Fact]
    public async Task PathPrefix_gateway_owns_prefix_and_host_owns_the_rest()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "from-upstream");

        await using var app = await StartEmbeddedAsync(
            o => { Minimal(o, upstream.BaseUrl, "/api/{**catch-all}"); o.PathPrefix = "/api"; },
            configureApp: a => a.MapGet("/outside", () => "from-host"));

        var client = app.GetTestClient();

        var api = await client.GetAsync("/api/thing");
        Assert.Equal("from-upstream", await api.Content.ReadAsStringAsync());

        var host = await client.GetAsync("/outside");
        Assert.Equal("from-host", await host.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InMemoryRoutes_are_used_without_reading_a_file()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "in-memory-routed");

        await using var app = await StartEmbeddedAsync(o => Minimal(o, upstream.BaseUrl));

        var resp = await app.GetTestClient().GetAsync("/anything");
        Assert.Equal("in-memory-routed", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_endpoints_served_when_enabled()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var app = await StartEmbeddedAsync(o =>
        {
            Minimal(o, upstream.BaseUrl);
            o.MapHealthEndpoints = true;
        });

        var client = app.GetTestClient();
        Assert.Equal("OK",    await (await client.GetAsync("/healthz")).Content.ReadAsStringAsync());
        Assert.Equal("Ready", await (await client.GetAsync("/readyz")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_endpoints_not_intercepted_when_disabled()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "proxied");

        await using var app = await StartEmbeddedAsync(o => Minimal(o, upstream.BaseUrl));

        var resp = await app.GetTestClient().GetAsync("/healthz");
        Assert.Equal("proxied", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_api_not_intercepted_when_disabled()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "proxied");

        await using var app = await StartEmbeddedAsync(o => Minimal(o, upstream.BaseUrl));

        var resp = await app.GetTestClient().GetAsync("/admin/routes/reload");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("proxied", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Observability_can_be_enabled_on_an_embedded_gateway()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "traced");

        await using var app = await StartEmbeddedAsync(
            o =>
            {
                o.Routes                    = CatchAll(upstream.BaseUrl);
                o.ConfigureObservability    = true;
                o.EnablePluginDirectoryScan = false;
                o.EnableAdminApi            = false;
                o.MapHealthEndpoints        = false;
            },
            settings: new Dictionary<string, string?>
            {
                ["Gateway:Observability:Console:Enabled"] = "true",
            });

        var resp = await app.GetTestClient().GetAsync("/x");
        Assert.Equal("traced", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task File_exporter_is_wired_when_enabled()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "filed");

        var tracesPath = Path.Combine(Path.GetTempPath(), $"cs-traces-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var app = await StartEmbeddedAsync(
                o =>
                {
                    o.Routes                    = CatchAll(upstream.BaseUrl);
                    o.ConfigureObservability    = true;
                    o.EnablePluginDirectoryScan = false;
                    o.EnableAdminApi            = false;
                    o.MapHealthEndpoints        = false;
                },
                settings: new Dictionary<string, string?>
                {
                    ["Gateway:Observability:File:Enabled"]    = "true",
                    ["Gateway:Observability:File:TracesPath"] = tracesPath,
                });

            var resp = await app.GetTestClient().GetAsync("/x");
            Assert.Equal("filed", await resp.Content.ReadAsStringAsync());
        }
        finally
        {
            if (File.Exists(tracesPath)) File.Delete(tracesPath);
        }
    }

    [Fact]
    public async Task Per_route_mTLS_client_certificate_is_loaded_from_a_pfx_file()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.RespondWith(200, "mtls-proxied");

        var pfxPath = Path.Combine(Path.GetTempPath(), $"cs-mtls-{Guid.NewGuid():N}.pfx");
        using (var rsa = RSA.Create(2048))
        {
            var req  = new CertificateRequest("CN=conduitsharp-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            await File.WriteAllBytesAsync(pfxPath, cert.Export(X509ContentType.Pkcs12));
        }

        try
        {
            await using var app = await StartEmbeddedAsync(
                o => Minimal(o, upstream.BaseUrl),
                settings: new Dictionary<string, string?>
                {
                    ["Gateway:Tls:ClientCertificates:0:RouteId"] = "embedded-test",
                    ["Gateway:Tls:ClientCertificates:0:Path"]    = pfxPath,
                });

            var resp = await app.GetTestClient().GetAsync("/anything");
            Assert.Equal("mtls-proxied", await resp.Content.ReadAsStringAsync());
        }
        finally
        {
            if (File.Exists(pfxPath)) File.Delete(pfxPath);
        }
    }

    [Fact]
    public async Task Per_route_mTLS_certificate_that_cannot_be_loaded_fails_the_gateway_at_startup()
    {
        await using var upstream = await FakeUpstream.StartAsync();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => StartEmbeddedAsync(
            o => Minimal(o, upstream.BaseUrl),
            settings: new Dictionary<string, string?>
            {
                ["Gateway:Tls:ClientCertificates:0:RouteId"] = "embedded-test",
                ["Gateway:Tls:ClientCertificates:0:Path"]    = "/does/not/exist.pfx",
            }));

        Assert.Contains("exist.pfx", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Per_route_mTLS_certificate_with_neither_path_nor_thumbprint_fails_at_startup()
    {
        await using var upstream = await FakeUpstream.StartAsync();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => StartEmbeddedAsync(
            o => Minimal(o, upstream.BaseUrl),
            settings: new Dictionary<string, string?>
            {
                ["Gateway:Tls:ClientCertificates:0:RouteId"] = "embedded-test",
            }));

        Assert.Contains("path", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("storeThumbprint", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Client_cert_plus_dangerousAcceptAnyServerCertificate_on_the_same_route_fails_fast()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gateway:Tls:ClientCertificates:0:RouteId"] = "mtls-route",
            ["Gateway:Tls:ClientCertificates:0:Path"]    = "/does/not/matter.pfx",
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddConduitSharpGateway(o =>
            {
                o.ConfigureObservability    = false;
                o.EnablePluginDirectoryScan = false;
                o.Routes = new GatewayRoutesConfiguration
                {
                    Routes =
                    {
                        new GatewayRoute
                        {
                            Id      = "mtls-route",
                            Route   = new RouteConfig { Match = new RouteMatch { Path = "/{**catch-all}" } },
                            Cluster = new ClusterConfig
                            {
                                Destinations = new Dictionary<string, DestinationConfig>
                                {
                                    ["node-0"] = new() { Address = "https://upstream:443" },
                                },
                                HttpClient = new HttpClientConfig { DangerousAcceptAnyServerCertificate = true },
                            },
                        },
                    },
                };
            }));

        Assert.Contains("dangerousAcceptAnyServerCertificate", ex.Message);
        Assert.Contains("mutually exclusive", ex.Message);
    }
}

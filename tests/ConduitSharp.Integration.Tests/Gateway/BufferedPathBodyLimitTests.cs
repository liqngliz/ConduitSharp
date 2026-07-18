using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using ConduitSharp.Gateway;
using Xunit;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Integration.Tests.Helpers;
using ConduitSharp.Core.Routing;

namespace ConduitSharp.Integration.Tests.Gateway;

public sealed class BufferedPathBodyLimitTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;
    // Used to capture what the gateway set the MaxRequestBodySize feature to
    public static long? CapturedMaxRequestBodySize { get; set; } = -1;

    public async Task InitializeAsync()
    {
        _upstream = await FakeUpstream.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _upstream.DisposeAsync();
    }

    [Fact]
    public async Task BufferedRoute_WithLimit_SetsFeatureLimit()
    {
        CapturedMaxRequestBodySize = -1; // Reset

        var routes = """
        {
        "routes": [
            {
                "id": "buffered-route",
                "route": { "match": { "path": "/test" } },
                "maxRequestBodyBytes": 52428800,
                "cluster": {
                    "loadBalancingPolicy": "RoundRobin",
                    "destinations": { "node-0": { "address": "UPSTREAM_URL" } }
                },
                "retry": { "maxAttempts": 2 },
                "plugins": []
            }
        ]
        }
        """.Replace("UPSTREAM_URL", _upstream.BaseUrl);

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes, configureWebHost: builder => 
        {
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, CaptureFeatureStartupFilter>());
        });
        
        using var client = factory.CreateClient();
        var content = new StringContent("test body");
        await client.PutAsync("/test", content);

        Assert.Equal(52428800, CapturedMaxRequestBodySize);
    }

    [Fact]
    public async Task BufferedRoute_WithUnlimited_SetsFeatureNull()
    {
        CapturedMaxRequestBodySize = -1;

        var routes = """
        {
        "routes": [
            {
                "id": "buffered-route",
                "route": { "match": { "path": "/test" } },
                "maxRequestBodyBytes": 0,
                "cluster": {
                    "loadBalancingPolicy": "RoundRobin",
                    "destinations": { "node-0": { "address": "UPSTREAM_URL" } }
                },
                "retry": { "maxAttempts": 2 },
                "plugins": []
            }
        ]
        }
        """.Replace("UPSTREAM_URL", _upstream.BaseUrl);

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes, configureWebHost: builder => 
        {
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, CaptureFeatureStartupFilter>());
        });
        
        using var client = factory.CreateClient();
        var content = new StringContent("test body");
        await client.PutAsync("/test", content);

        Assert.Null(CapturedMaxRequestBodySize);
    }

    [Fact]
    public async Task StreamingRoute_WithLimit_SetsFeatureLimit()
    {
        CapturedMaxRequestBodySize = -1;

        var routes = """
        {
        "routes": [
            {
                "id": "streaming-route",
                "route": { "match": { "path": "/test" } },
                "maxRequestBodyBytes": 52428800,
                "cluster": {
                    "loadBalancingPolicy": "RoundRobin",
                    "destinations": { "node-0": { "address": "UPSTREAM_URL" } }
                },
                "plugins": []
            }
        ]
        }
        """.Replace("UPSTREAM_URL", _upstream.BaseUrl);

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes, configureWebHost: builder => 
        {
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, CaptureFeatureStartupFilter>());
        });
        
        using var client = factory.CreateClient();
        var content = new StringContent("test body");
        await client.PutAsync("/test", content);

        Assert.Equal(52428800, CapturedMaxRequestBodySize);
    }

    [Fact]
    public async Task BothPaths_WithNegativeLimit_DoNotModifyFeature()
    {
        CapturedMaxRequestBodySize = -1;

        var routes = """
        {
        "routes": [
            {
                "id": "streaming-route",
                "route": { "match": { "path": "/test-stream" } },
                "maxRequestBodyBytes": -1,
                "cluster": {
                    "loadBalancingPolicy": "RoundRobin",
                    "destinations": { "node-0": { "address": "UPSTREAM_URL" } }
                },
                "plugins": []
            },
            {
                "id": "buffered-route",
                "route": { "match": { "path": "/test-buffer" } },
                "maxRequestBodyBytes": -1,
                "cluster": {
                    "loadBalancingPolicy": "RoundRobin",
                    "destinations": { "node-0": { "address": "UPSTREAM_URL" } }
                },
                "retry": { "maxAttempts": 2 },
                "plugins": []
            }
        ]
        }
        """.Replace("UPSTREAM_URL", _upstream.BaseUrl);

        await using var factory = await GatewayFactory.CreateAsync(_upstream, routes, configureWebHost: builder => 
        {
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, CaptureFeatureStartupFilter>());
        });
        
        using var client = factory.CreateClient();
        
        await client.PutAsync("/test-stream", new StringContent("test body"));
        Assert.Equal(-2, CapturedMaxRequestBodySize); // Our mock feature returns -2

        CapturedMaxRequestBodySize = -1;
        await client.PutAsync("/test-buffer", new StringContent("test body"));
        Assert.Equal(-2, CapturedMaxRequestBodySize);
    }

    private class CaptureFeatureStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, pipelineNext) =>
                {
                    var mockFeature = new MockHttpMaxRequestBodySizeFeature();
                    context.Features.Set<IHttpMaxRequestBodySizeFeature>(mockFeature);
                    
                    await pipelineNext(context);

                    BufferedPathBodyLimitTests.CapturedMaxRequestBodySize = mockFeature.MaxRequestBodySize;
                });

                next(app);
            };
        }
    }

    private class MockHttpMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; } = -2;
    }
    [Fact]
    [Trait("Category", "Security")]
    public async Task BufferedRoute_AboveKestrelDefault_EndToEnd_HonorsConfiguredLimit()
    {
        // 1. Configure the route with limit 40MB (above Kestrel's 30MB default)
        var routes = """
        {
        "routes": [
            {
                "id": "buffered-route",
                "route": { "match": { "path": "/test" } },
                "maxRequestBodyBytes": 41943040,
                "cluster": {
                    "loadBalancingPolicy": "RoundRobin",
                    "destinations": { "node-0": { "address": "UPSTREAM_URL" } }
                },
                "retry": { "maxAttempts": 2 }
            }
        ]
        }
        """.Replace("UPSTREAM_URL", _upstream.BaseUrl);

        var path = System.IO.Path.GetTempFileName();
        await System.IO.File.WriteAllTextAsync(path, routes);

        // 2. Start a real Kestrel host on a random port
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Environment.EnvironmentName = "Test";
        builder.Configuration.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["Gateway:RoutesPath"] = path
        });
        builder.AddConduitSharpGateway();

        await using var app = builder.Build();
        app.UseConduitSharpGateway();
        await app.StartAsync();

        // 3. Get the bound port
        var serverAddress = app.Urls.First();

        // 4. Send a 35MB body (below 40MB but above 30MB Kestrel limit)
        // If the bug is present, Kestrel will throw 413 Payload Too Large.
        // If the bug is fixed, it will forward successfully and return 200 OK.
        using var client = new HttpClient();
        client.BaseAddress = new Uri(serverAddress);
        
        var request = new HttpRequestMessage(HttpMethod.Put, "/test");
        var hugeBody = new byte[35 * 1024 * 1024]; // 35 MB
        request.Content = new ByteArrayContent(hugeBody);

        var response = await client.SendAsync(request);

        // Prove that the request made it through Kestrel's 30MB default and was successfully forwarded!
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Clean up
        await app.StopAsync();
        System.IO.File.Delete(path);
    }

}

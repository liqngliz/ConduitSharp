using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway;
using ConduitSharp.Gateway.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace ConduitSharp.Benchmarks;

/// <summary>
/// Boots the full gateway in-proc (TestServer, no sockets) with YARP's outbound
/// client replaced by an in-memory upstream — so benchmarks measure the gateway,
/// not the network stack.
/// </summary>
internal static class BenchGateway
{
    /// <summary>Fake cluster address; never dialed — the in-memory handler answers.</summary>
    public const string UpstreamAddress = "http://bench-upstream/";

    public static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        GatewayRoutesConfiguration routes,
        IEnumerable<IPipelinePlugin>? plugins = null,
        IDictionary<string, string?>? settings = null,
        bool realForwarder = false) // true: keep YARP's socket client (comparison benches)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (settings is not null)
            builder.Configuration.AddInMemoryCollection(settings);

        builder.AddConduitSharpGateway(o =>
        {
            o.Routes                    = routes;
            o.ConfigureObservability    = false;
            o.EnablePluginDirectoryScan = false;
            o.EnableAdminApi            = false;
            o.MapHealthEndpoints        = false;
        });

        if (plugins is not null)
            foreach (var plugin in plugins)
                builder.Services.AddSingleton(plugin);

        if (!realForwarder)
            builder.Services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(
                new InMemoryUpstreamFactory()));

        var app = builder.Build();
        app.UseConduitSharpGateway();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    public static GatewayRoute Route(
        string id,
        string path,
        bool withCluster = false,
        bool streamOnly = false,
        long? maxRequestBodyBytes = null,
        string upstreamAddress = UpstreamAddress,
        bool withRetry = false, // forces the buffered body path (for idempotent methods)
        params PluginConfig[] plugins) => new()
    {
        Id         = id,
        Route      = new RouteConfig { Match = new RouteMatch { Path = path } },
        StreamOnly = streamOnly,
        MaxRequestBodyBytes = maxRequestBodyBytes,
        Retry      = withRetry ? new RetryConfig { MaxAttempts = 2 } : null,
        Cluster    = withCluster
            ? new ClusterConfig
              {
                  Destinations = new Dictionary<string, DestinationConfig>
                  {
                      ["node-0"] = new() { Address = upstreamAddress },
                  },
              }
            : null,
        Plugins = [.. plugins],
    };

    public static PluginConfig Custom(string variant, int order) => new()
    {
        Name    = PluginName.Custom,
        Variant = variant,
        Order   = order,
    };
}

/// <summary>Replaces YARP's SocketsHttpHandler: drains the request body, returns 200 + small JSON.</summary>
internal sealed class InMemoryUpstreamFactory : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
        new(new UpstreamHandler(), disposeHandler: true);

    private sealed class UpstreamHandler : HttpMessageHandler
    {
        private static readonly byte[] OkBody = "{\"ok\":true}"u8.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                await request.Content.CopyToAsync(Stream.Null, cancellationToken);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content        = new ByteArrayContent(OkBody),
            };
        }
    }
}

/// <summary>Terminal plugin: writes 200 "ok" without calling next — no forward, no upstream.</summary>
internal sealed class ResponderPlugin : IPipelinePlugin
{
    public PluginName Name    => PluginName.Custom;
    public string Id          => "bench-responder";
    public string? Variant    => "bench-responder";

    public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
    {
        context.Response.StatusCode  = 200;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("ok");
    }
}

/// <summary>Pass-through plugin: measures pure per-plugin dispatch overhead.</summary>
internal sealed class NoopPlugin : IPipelinePlugin
{
    public PluginName Name    => PluginName.Custom;
    public string Id          => "bench-noop";
    public string? Variant    => "bench-noop";

    public Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next) =>
        next(context);
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

namespace ConduitSharp.Benchmarks;

/// <summary>
/// Shared pieces for the head-to-head comparison benches: a real loopback Kestrel
/// upstream (1 KB response, drains request bodies) and an in-proc Ocelot host.
/// Both gateways pay the identical socket cost to the same upstream — the measured
/// delta is gateway overhead. APISIX is nginx/Lua: macro rig only, never here.
/// </summary>
internal static class ComparisonRig
{
    /// <summary>Bench-only HS256 secret shared by both gateways' JWT setups.</summary>
    public static readonly byte[] JwtSecret =
        Encoding.ASCII.GetBytes("conduitsharp-microbench-signing-key-0123456789");

    public static async Task<(WebApplication App, string Url)> StartUpstreamAsync()
    {
        var payload = Encoding.ASCII.GetBytes(new string('x', 1024));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Run(async ctx =>
        {
            if (ctx.Request.ContentLength is > 0)
                await ctx.Request.Body.CopyToAsync(Stream.Null);
            ctx.Response.StatusCode = 200;
            await ctx.Response.Body.WriteAsync(payload);
        });
        await app.StartAsync();
        return (app, app.Urls.First());
    }

    public static async Task<(WebApplication App, HttpClient Client)> StartOcelotAsync(
        string configJson, Action<WebApplicationBuilder>? configure = null,
        Action<IOcelotBuilder>? configureOcelot = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configJson)));
        var ocelot = builder.Services.AddOcelot(builder.Configuration);
        configureOcelot?.Invoke(ocelot);
        configure?.Invoke(builder);

        var app = builder.Build();
        await app.UseOcelot();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    /// <summary>One Ocelot route block; extraJson is spliced verbatim (e.g. auth/rate-limit options).</summary>
    public static string OcelotRoute(string template, string upstreamUrl, string method = "Get", string extraJson = "")
    {
        var uri = new Uri(upstreamUrl);
        return $$"""
            {
              "UpstreamPathTemplate": "{{template}}",
              "UpstreamHttpMethod": [ "{{method}}" ],
              "DownstreamPathTemplate": "{{template}}",
              "DownstreamScheme": "http",
              "DownstreamHostAndPorts": [ { "Host": "{{uri.Host}}", "Port": {{uri.Port}} } ]{{extraJson}}
            }
            """;
    }

    public static string OcelotConfig(IEnumerable<string> routes, string globalJson = "{}") =>
        $$"""{ "Routes": [ {{string.Join(",", routes)}} ], "GlobalConfiguration": {{globalJson}} }""";

    /// <summary>HS256 token signed with <see cref="JwtSecret"/> (sub=bench, exp +1 day).</summary>
    public static string SignHs256Token()
    {
        static string B64U(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header  = B64U("""{"alg":"HS256","typ":"JWT"}"""u8.ToArray());
        var exp     = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
        var payload = B64U(Encoding.UTF8.GetBytes($$"""{"sub":"bench","exp":{{exp}}}"""));
        using var hmac = new HMACSHA256(JwtSecret);
        var sig = B64U(hmac.ComputeHash(Encoding.ASCII.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{sig}";
    }
}

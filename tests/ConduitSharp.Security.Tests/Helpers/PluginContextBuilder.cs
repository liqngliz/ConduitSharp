using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using ConduitSharp.Core.Routing;

namespace ConduitSharp.Security.Tests.Helpers;

internal static class HttpContextBuilder
{
    internal static HttpContext Make(
        string method = "GET",
        string path   = "/test",
        Dictionary<string, string>? headers = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        if (headers is not null)
        {
            foreach (var kvp in headers)
                context.Request.Headers[kvp.Key] = new StringValues(kvp.Value);
        }

        context.Items["ConduitSharp.RouteId"] = "test-route";

        return context;
    }

    internal static HttpContext WithAuth(string authHeader) =>
        Make(headers: new Dictionary<string, string> { ["Authorization"] = authHeader });

    internal static HttpContext WithHeader(string name, string value) =>
        Make(headers: new Dictionary<string, string> { [name] = value });

    internal static HttpContext NoHeaders() => Make();

    internal static RequestDelegate NoOpNext() => _ => Task.CompletedTask;

    internal static (RequestDelegate next, Func<bool> wasCalled) TrackingNext()
    {
        var called = false;
        return (_ => { called = true; return Task.CompletedTask; }, () => called);
    }
}

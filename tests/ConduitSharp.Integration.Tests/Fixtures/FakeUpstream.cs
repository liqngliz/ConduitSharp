namespace ConduitSharp.Integration.Tests.Fixtures;

/// <summary>
/// A lightweight in-process HTTP server that stands in for upstream services.
/// Start it before the gateway so you can embed its URL in the test routes.json.
/// </summary>
public sealed class FakeUpstream : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Lock _requestsLock = new();
    private Func<HttpContext, Task> _handler;

    public string BaseUrl { get; }
    // Guarded by _requestsLock: concurrent test requests dispatch in parallel.
    public List<FakeRequest> ReceivedRequests { get; } = [];

    private FakeUpstream(WebApplication app, string baseUrl)
    {
        _app     = app;
        BaseUrl  = baseUrl;
        _handler = DefaultHandler;
    }

    /// <summary>Starts a fake upstream on a random available port.</summary>
    public static async Task<FakeUpstream> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        // The fake never imposes a body limit: tests exercise the gateway's limits,
        // and Kestrel's 30 MB default here would 413 large uploads before they matter.
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = null);
        builder.Logging.ClearProviders();

        var app = builder.Build();

        // instance is captured by reference so the closure sees the final value
        // once assigned below, before any request can arrive.
        FakeUpstream? instance = null;
        app.Run(ctx => instance!.DispatchAsync(ctx));
        await app.StartAsync();

        var address = app.Urls.First();
        instance = new FakeUpstream(app, address);
        return instance;
    }

    /// <summary>Override the response for the next request(s).</summary>
    public void RespondWith(Func<HttpContext, Task> handler) => _handler = handler;

    /// <summary>Return a fixed status + body for all requests.</summary>
    public void RespondWith(int statusCode, string body = "", string contentType = "application/json")
    {
        _handler = async ctx =>
        {
            ctx.Response.StatusCode  = statusCode;
            ctx.Response.ContentType = contentType;
            await ctx.Response.WriteAsync(body);
        };
    }

    /// <summary>Reset captured requests and restore the default 200 OK handler.</summary>
    public void Reset()
    {
        lock (_requestsLock)
            ReceivedRequests.Clear();
        _handler = DefaultHandler;
    }

    private async Task DispatchAsync(HttpContext ctx)
    {
        var captured = await FakeRequest.CaptureAsync(ctx);
        lock (_requestsLock)
            ReceivedRequests.Add(captured);
        await _handler(ctx);
    }

    private static async Task DefaultHandler(HttpContext ctx)
    {
        ctx.Response.StatusCode  = 200;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"ok\":true}");
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

/// <summary>Snapshot of a request received by the fake upstream.</summary>
public sealed class FakeRequest
{
    public string Method  { get; init; } = "";
    public string Path    { get; init; } = "";
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public string Body    { get; init; } = "";

    public static async Task<FakeRequest> CaptureAsync(HttpContext ctx)
    {
        ctx.Request.EnableBuffering();
        using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;

        return new FakeRequest
        {
            Method  = ctx.Request.Method,
            Path    = ctx.Request.Path.Value ?? "/",
            Headers = ctx.Request.Headers
                .ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            Body    = body,
        };
    }
}

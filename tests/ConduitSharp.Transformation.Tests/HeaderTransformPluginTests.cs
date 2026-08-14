using System.Text.Json;
using Xunit;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Transformation.Plugins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
namespace ConduitSharp.Transformation.Tests;

public sealed class HeaderTransformPluginTests
{
    private static readonly RequestDelegate NoOp = _ => Task.CompletedTask;

    private static HttpContext MakeContext(Dictionary<string, string>? requestHeaders = null)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new FiringResponseFeature());
        context.Request.Method = "GET";
        context.Request.Path = "/test";
        if (requestHeaders is not null)
            foreach (var (k, v) in requestHeaders)
                context.Request.Headers[k] = v;
        return context;
    }

    private static JsonElement Config(string json) => JsonDocument.Parse(json).RootElement;

    private static Task StartResponse(HttpContext context) =>
        ((FiringResponseFeature)context.Features.Get<IHttpResponseFeature>()!).FireOnStartingAsync();

    private sealed class FiringResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> cb, object state)> _onStarting = [];
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }
        public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) { }

        public async Task FireOnStartingAsync()
        {
            HasStarted = true;
            for (var i = _onStarting.Count - 1; i >= 0; i--)
                await _onStarting[i].cb(_onStarting[i].state);
        }
    }

    [Fact]
    public void Name_IsHeaderTransform() =>
        Assert.Equal(PluginName.HeaderTransform, new HeaderTransformPlugin().Name);

    [Fact]
    public async Task Request_Add_HeaderAbsent_AddsHeader()
    {
        var context = MakeContext();
        await new HeaderTransformPlugin().ExecuteAsync(context, Config("""{"request":{"add":{"X-Source":"gateway"}}}"""), NoOp);
        Assert.Equal("gateway", context.Request.Headers["X-Source"].ToString());
    }

    [Fact]
    public async Task Request_Add_HeaderPresent_DoesNotOverwrite()
    {
        var context = MakeContext(new() { ["X-Source"] = "original" });
        await new HeaderTransformPlugin().ExecuteAsync(context, Config("""{"request":{"add":{"X-Source":"gateway"}}}"""), NoOp);
        Assert.Equal("original", context.Request.Headers["X-Source"].ToString());
    }

    [Fact]
    public async Task Request_Set_Overwrites()
    {
        var context = MakeContext(new() { ["X-Forwarded-By"] = "old" });
        await new HeaderTransformPlugin().ExecuteAsync(context, Config("""{"request":{"set":{"X-Forwarded-By":"conduit"}}}"""), NoOp);
        Assert.Equal("conduit", context.Request.Headers["X-Forwarded-By"].ToString());
    }

    [Fact]
    public async Task Request_Remove_IsCaseInsensitive()
    {
        var context = MakeContext(new(StringComparer.OrdinalIgnoreCase) { ["x-debug"] = "true" });
        await new HeaderTransformPlugin().ExecuteAsync(context, Config("""{"request":{"remove":["X-Debug"]}}"""), NoOp);
        Assert.False(context.Request.Headers.ContainsKey("x-debug"));
    }

    [Fact]
    public async Task Request_RemoveThenAdd_RemoveRunsFirst()
    {
        var context = MakeContext(new() { ["X-Foo"] = "old" });
        await new HeaderTransformPlugin().ExecuteAsync(context, Config("""{"request":{"remove":["X-Foo"],"add":{"X-Foo":"new"}}}"""), NoOp);
        Assert.Equal("new", context.Request.Headers["X-Foo"].ToString());
    }

    [Fact]
    public async Task Response_Remove_StripsUpstreamHeaderBeforeSend()
    {
        var context = MakeContext();
        context.Response.Headers["Server"] = "kestrel";
        context.Response.Headers["X-Powered-By"] = "upstream";

        await new HeaderTransformPlugin().ExecuteAsync(context, Config("""{"response":{"remove":["Server","X-Powered-By"]}}"""), NoOp);
        await StartResponse(context);

        Assert.False(context.Response.Headers.ContainsKey("Server"));
        Assert.False(context.Response.Headers.ContainsKey("X-Powered-By"));
    }

    [Fact]
    public async Task Response_Set_AddsHeaderBeforeSend()
    {
        var context = MakeContext();
        await new HeaderTransformPlugin().ExecuteAsync(context, Config("""{"response":{"set":{"X-Frame-Options":"DENY"}}}"""), NoOp);
        await StartResponse(context);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
    }

    [Fact]
    public async Task Response_NotAppliedUntilResponseStarts()
    {
        var context = MakeContext();
        context.Response.Headers["Server"] = "kestrel";
        await new HeaderTransformPlugin().ExecuteAsync(context, Config("""{"response":{"remove":["Server"]}}"""), NoOp);
        Assert.True(context.Response.Headers.ContainsKey("Server"));
    }

    [Fact]
    public async Task RequestAndResponse_BothApplied()
    {
        var context = MakeContext(new() { ["X-Internal"] = "secret" });
        context.Response.Headers["Server"] = "kestrel";

        await new HeaderTransformPlugin().ExecuteAsync(
            context,
            Config("""{"request":{"remove":["X-Internal"]},"response":{"remove":["Server"]}}"""),
            NoOp);
        await StartResponse(context);

        Assert.False(context.Request.Headers.ContainsKey("X-Internal"));
        Assert.False(context.Response.Headers.ContainsKey("Server"));
    }

    [Fact]
    public async Task EmptyConfig_PassesThrough_AndCallsNext()
    {
        var called = false;
        RequestDelegate next = _ => { called = true; return Task.CompletedTask; };
        var context = MakeContext(new() { ["Accept"] = "application/json" });

        await new HeaderTransformPlugin().ExecuteAsync(context, Config("{}"), next);

        Assert.Equal("application/json", context.Request.Headers["Accept"].ToString());
        Assert.True(called);
    }

    [Theory]
    [InlineData("""{"add":{"X-A":"1"}}""")]
    [InlineData("""{"set":{"X-A":"1"}}""")]
    [InlineData("""{"remove":["X-A"]}""")]
    public void ValidateConfig_FlatShape_ThrowsWithMigrationHint(string flat)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HeaderTransformPlugin().ValidateConfig(Config(flat)));
        Assert.Contains("flat shape", ex.Message);
        Assert.Contains("request", ex.Message);
        Assert.Contains("response", ex.Message);
    }

    [Fact]
    public void ValidateConfig_NestedShape_DoesNotThrow() =>
        new HeaderTransformPlugin().ValidateConfig(
            Config("""{"request":{"remove":["X-Debug"]},"response":{"remove":["Server"]}}"""));

    [Fact]
    public void ValidateConfig_Empty_DoesNotThrow() =>
        new HeaderTransformPlugin().ValidateConfig(Config("{}"));
}

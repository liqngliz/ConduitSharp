using System.Text.Json;
using Xunit;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Transformation.Plugins;
using Microsoft.AspNetCore.Http;
namespace ConduitSharp.Transformation.Tests;

public sealed class HeaderTransformPluginTests
{
    private static readonly RequestDelegate NoOp = _ => Task.CompletedTask;

    private static HttpContext MakeContext(
        Dictionary<string, string>? headers = null)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/test";

        if (headers is not null)
        {
            foreach (var kvp in headers)
                context.Request.Headers[kvp.Key] = kvp.Value;
        }

        return context;
    }

    private static JsonElement MakeConfig(string configJson = "{}")
    {
        return JsonDocument.Parse(configJson).RootElement;
    }

    // -------------------------------------------------------------------------
    // Name
    // -------------------------------------------------------------------------

    [Fact]
    public void Name_IsHeaderTransform()
    {
        Assert.Equal(PluginName.HeaderTransform, new HeaderTransformPlugin().Name);
    }

    // -------------------------------------------------------------------------
    // add — only if absent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Add_HeaderAbsent_AddsHeader()
    {
        var context = MakeContext();
        var config = MakeConfig("""{"add":{"X-Source":"gateway"}}""");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
        Assert.Equal("gateway", context.Request.Headers["X-Source"].ToString());
    }

    [Fact]
    public async Task Add_HeaderAlreadyPresent_DoesNotOverwrite()
    {
        var context = MakeContext(
            headers: new() { ["X-Source"] = "original" });
        var config = MakeConfig("""{"add":{"X-Source":"gateway"}}""");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
        Assert.Equal("original", context.Request.Headers["X-Source"].ToString());
    }

    [Fact]
    public async Task Add_MultipleHeaders_AllAdded()
    {
        var context = MakeContext();
        var config = MakeConfig("""{"add":{"X-A":"1","X-B":"2"}}""");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
        Assert.Equal("1", context.Request.Headers["X-A"].ToString());
        Assert.Equal("2", context.Request.Headers["X-B"].ToString());
    }

    // -------------------------------------------------------------------------
    // set — unconditional upsert
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Set_HeaderAbsent_AddsHeader()
    {
        var context = MakeContext();
        var config = MakeConfig("""{"set":{"X-Forwarded-By":"conduit"}}""");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
        Assert.Equal("conduit", context.Request.Headers["X-Forwarded-By"].ToString());
    }

    [Fact]
    public async Task Set_HeaderPresent_Overwrites()
    {
        var context = MakeContext(
            headers: new() { ["X-Forwarded-By"] = "old" });
        var config = MakeConfig("""{"set":{"X-Forwarded-By":"conduit"}}""");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
        Assert.Equal("conduit", context.Request.Headers["X-Forwarded-By"].ToString());
    }

    // -------------------------------------------------------------------------
    // remove
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Remove_HeaderPresent_RemovesIt()
    {
        var context = MakeContext(
            headers: new() { ["X-Internal-Token"] = "secret" });
        var config = MakeConfig("""{"remove":["X-Internal-Token"]}""");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
        Assert.False(context.Request.Headers.ContainsKey("X-Internal-Token"));
    }

    [Fact]
    public async Task Remove_HeaderAbsent_NoError()
    {
        var context = MakeContext();
        var config = MakeConfig("""{"remove":["X-Does-Not-Exist"]}""");
        // Should not throw
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
    }

    [Fact]
    public async Task Remove_IsCaseInsensitive()
    {
        var context = MakeContext(
            headers: new(StringComparer.OrdinalIgnoreCase) { ["x-debug"] = "true" });
        var config = MakeConfig("""{"remove":["X-Debug"]}""");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
        Assert.False(context.Request.Headers.ContainsKey("x-debug"));
    }

    // -------------------------------------------------------------------------
    // Order: remove happens before add/set
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAndAdd_RemoveRunsFirst()
    {
        // Remove "X-Foo" then add it back with a new value.
        var context = MakeContext(
            headers: new() { ["X-Foo"] = "old" });
        var config = MakeConfig("""{"remove":["X-Foo"],"add":{"X-Foo":"new"}}""");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
        Assert.Equal("new", context.Request.Headers["X-Foo"].ToString());
    }

    // -------------------------------------------------------------------------
    // Empty config — no-op
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmptyConfig_PassesThrough()
    {
        var context = MakeContext(
            headers: new() { ["Accept"] = "application/json" });
        var config = MakeConfig("{}");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, NoOp);
        Assert.Equal("application/json", context.Request.Headers["Accept"].ToString());
    }

    // -------------------------------------------------------------------------
    // next is always called
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_AlwaysCallsNext()
    {
        var called  = false;
        Microsoft.AspNetCore.Http.RequestDelegate next = _ => { called = true; return Task.CompletedTask; };
        var context = MakeContext();
        var config = MakeConfig("{}");
        await new HeaderTransformPlugin().ExecuteAsync(context, config, next);
        Assert.True(called);
    }
}

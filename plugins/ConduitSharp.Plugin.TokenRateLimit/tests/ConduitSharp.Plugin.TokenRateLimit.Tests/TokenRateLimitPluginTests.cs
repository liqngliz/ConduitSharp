using System.Text;
using System.Text.Json;
using ConduitSharp.Traffic.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ConduitSharp.Plugin.TokenRateLimit.Tests;

public sealed class TokenRateLimitPluginTests
{
    private static (DefaultHttpContext Ctx, MemoryStream Sink, IRateLimitStore Store) NewContext(IRateLimitStore? store = null)
    {
        store ??= new InMemoryRateLimitStore();
        var services = new ServiceCollection();
        services.AddSingleton(store);
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Items["ConduitSharp.RouteId"] = "route-a";
        var sink = new MemoryStream();
        ctx.Response.Body = sink;
        return (ctx, sink, store);
    }

    private static JsonElement Config(string json) => JsonDocument.Parse(json).RootElement;
    private static RequestDelegate Forward(string body) => ctx => ctx.Response.WriteAsync(body);
    private static string Read(MemoryStream s) => Encoding.UTF8.GetString(s.ToArray());
    private static long WindowId(int windowSeconds = 60) => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowSeconds;

    private const string OpenAiFields = """["usage.prompt_tokens","usage.completion_tokens"]""";

    [Fact]
    public async Task ChargesSummedUsageFields_ToTheWindowCounter()
    {
        var (ctx, sink, store) = NewContext();
        var config = Config($$"""{ "maxTokensPerWindow": 10000, "usageFields": {{OpenAiFields}} }""");

        await new TokenRateLimitPlugin().ExecuteAsync(ctx, config,
            Forward("""{"choices":[],"usage":{"prompt_tokens":40,"completion_tokens":110}}"""));

        Assert.Equal(150, store.Peek("route-a\0global", WindowId()));       // 40 + 110
        Assert.Contains("completion_tokens", Read(sink));                    // response passed through
    }

    [Fact]
    public async Task SecondRequest_OverBudget_Returns429_WithRetryAfter()
    {
        var (ctx, _, store) = NewContext();
        store.Add("route-a\0global", WindowId(), 60, 10000); // window already at budget

        var config = Config($$"""{ "maxTokensPerWindow": 10000, "usageFields": {{OpenAiFields}} }""");
        await new TokenRateLimitPlugin().ExecuteAsync(ctx, config, Forward("{}"));

        Assert.Equal(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
        Assert.True(ctx.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task PerCaller_ByHeader_SeparateBudgets()
    {
        var store = new InMemoryRateLimitStore();
        var config = Config($$"""{ "maxTokensPerWindow": 10000, "keyHeader": "X-Api-Key", "usageFields": {{OpenAiFields}} }""");

        var (a, _, _) = NewContext(store); a.Request.Headers["X-Api-Key"] = "alice";
        await new TokenRateLimitPlugin().ExecuteAsync(a, config, Forward("""{"usage":{"prompt_tokens":10,"completion_tokens":0}}"""));

        var (b, _, _) = NewContext(store); b.Request.Headers["X-Api-Key"] = "bob";
        await new TokenRateLimitPlugin().ExecuteAsync(b, config, Forward("""{"usage":{"prompt_tokens":99,"completion_tokens":0}}"""));

        Assert.Equal(10, store.Peek("route-a\0alice", WindowId()));
        Assert.Equal(99, store.Peek("route-a\0bob", WindowId()));
    }

    [Fact]
    public async Task PerCaller_ByJwtClaim_DecodesBearerSubWithoutValidating()
    {
        var (ctx, _, store) = NewContext();
        ctx.Request.Headers.Authorization = "Bearer " + FakeJwt("""{"sub":"user-42"}""");
        var config = Config($$"""{ "maxTokensPerWindow": 10000, "keyClaim": "sub", "usageFields": ["usage.total_tokens"] }""");

        await new TokenRateLimitPlugin().ExecuteAsync(ctx, config, Forward("""{"usage":{"total_tokens":7}}"""));

        Assert.Equal(7, store.Peek("route-a\0user-42", WindowId()));
    }

    [Theory]
    [InlineData("""["usageMetadata.totalTokenCount"]""", """{"usageMetadata":{"totalTokenCount":33}}""", 33)]  // Gemini
    [InlineData("""["prompt_eval_count","eval_count"]""", """{"prompt_eval_count":5,"eval_count":8}""", 13)]     // Ollama native
    public async Task WorksAcrossProviders_ByConfiguredFields(string fields, string body, long expected)
    {
        var (ctx, _, store) = NewContext();
        var config = Config($$"""{ "maxTokensPerWindow": 10000, "usageFields": {{fields}} }""");

        await new TokenRateLimitPlugin().ExecuteAsync(ctx, config, Forward(body));

        Assert.Equal(expected, store.Peek("route-a\0global", WindowId()));
    }

    [Fact]
    public async Task NonJsonBody_ChargesNothing()
    {
        var (ctx, sink, store) = NewContext();
        var config = Config($$"""{ "maxTokensPerWindow": 10000, "usageFields": {{OpenAiFields}} }""");

        // An SSE stream is not a single JSON document.
        await new TokenRateLimitPlugin().ExecuteAsync(ctx, config, Forward("data: {\"x\":1}\n\ndata: [DONE]\n\n"));

        Assert.Equal(0, store.Peek("route-a\0global", WindowId()));
        Assert.Contains("[DONE]", Read(sink)); // still streamed to the client
    }

    [Fact]
    public async Task MissingUsageFields_ChargesNothing_ButServesResponse()
    {
        var (ctx, sink, store) = NewContext();
        var config = Config($$"""{ "maxTokensPerWindow": 10000, "usageFields": {{OpenAiFields}} }""");

        await new TokenRateLimitPlugin().ExecuteAsync(ctx, config, Forward("""{"choices":[{"text":"hi"}]}"""));

        Assert.Equal(0, store.Peek("route-a\0global", WindowId()));
        Assert.Contains("hi", Read(sink));
    }

    [Fact]
    public void CaptureMemoryBytes_IsMaxResponseBytes()
    {
        var plugin = new TokenRateLimitPlugin();
        Assert.Equal(2048, plugin.CaptureMemoryBytes(Config("""{ "maxResponseBytes": 2048, "usageFields": ["x"] }""")));
        Assert.Equal(1024 * 1024, plugin.CaptureMemoryBytes(Config("""{ "usageFields": ["x"] }"""))); // default
    }

    [Theory]
    [InlineData("""{ "windowSeconds": 60, "usageFields": ["x"] }""")]                       // no maxTokens
    [InlineData("""{ "maxTokensPerWindow": 100, "usageFields": [] }""")]                    // empty fields
    [InlineData("""{ "maxTokensPerWindow": 100, "windowSeconds": 0, "usageFields": ["x"] }""")] // bad window
    public void ValidateConfig_Rejects_BadConfig(string json)
    {
        Assert.Throws<InvalidOperationException>(() => new TokenRateLimitPlugin().ValidateConfig(Config(json)));
    }

    [Fact]
    public void ValidateConfig_Valid_DoesNotThrow() =>
        new TokenRateLimitPlugin().ValidateConfig(Config($$"""{ "maxTokensPerWindow": 100, "windowSeconds": 60, "usageFields": {{OpenAiFields}} }"""));

    // Builds a structurally valid (unsigned) JWT: base64url(header).base64url(payload).sig
    private static string FakeJwt(string payloadJson)
    {
        static string B64Url(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64Url("""{"alg":"none"}""")}.{B64Url(payloadJson)}.sig";
    }
}

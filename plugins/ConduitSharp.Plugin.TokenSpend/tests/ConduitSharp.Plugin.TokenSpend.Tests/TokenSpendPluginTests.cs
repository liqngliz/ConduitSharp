using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ConduitSharp.Plugin.TokenSpend.Tests;

public sealed class TokenSpendPluginTests
{
    private sealed class FakeStore : ISpendStore
    {
        public List<SpendRecord> Rows { get; } = [];
        public void Add(SpendRecord record) => Rows.Add(record);
        public IReadOnlyList<SpendRecord> Read(DateTimeOffset from, DateTimeOffset to) => Rows;
        public string HashCaller(string rawKey) => rawKey;
    }

    private static (DefaultHttpContext Ctx, MemoryStream Sink, FakeStore Store) NewContext(string? requestBody = null)
    {
        var store = new FakeStore();
        var services = new ServiceCollection();
        services.AddSingleton<ISpendStore>(store);
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Items["ConduitSharp.RouteId"] = "route-a";
        if (requestBody is not null)
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        var sink = new MemoryStream();
        ctx.Response.Body = sink;
        return (ctx, sink, store);
    }

    private static JsonElement Config(string json) => JsonDocument.Parse(json).RootElement;
    private static RequestDelegate Forward(string body) => ctx => ctx.Response.WriteAsync(body);

    private static RequestDelegate ForwardStreaming(string body) => ctx =>
    {
        ctx.Response.ContentType = "text/event-stream";
        return ctx.Response.WriteAsync(body);
    };

    private const string OpenAi = """
        { "inputFields": ["usage.prompt_tokens"], "outputFields": ["usage.completion_tokens"] }
        """;

    private const string Anthropic = """
        { "inputFields": ["usage.input_tokens"], "outputFields": ["usage.output_tokens"],
          "cacheWriteFields": ["usage.cache_creation_input_tokens"],
          "cacheReadFields":  ["usage.cache_read_input_tokens"] }
        """;

    [Fact]
    public async Task MetadataFields_ReachThroughAJsonStringValue()
    {
        // Codex sends two calls per prompt against one key: the real turn, and a hidden one that
        // names the sidebar entry. thread_source is the only thing that tells them apart, and it
        // sits inside x-codex-turn-metadata, whose value is a JSON document held in a string.
        var (ctx, _, store) = NewContext("""
            {"model":"gpt-5.6-luna","input":[{"role":"user","content":"title this"}],
             "client_metadata":{
               "thread_id":"019fc46d",
               "x-codex-turn-metadata":"{\"request_kind\":\"turn\",\"thread_source\":\"system\"}"}}
            """);

        await new TokenSpendPlugin().ExecuteAsync(ctx, Config("""
            { "inputFields": ["usage.input_tokens"],
              "metadataFields": {
                "source":  "client_metadata.x-codex-turn-metadata.thread_source",
                "kind":    "client_metadata.x-codex-turn-metadata.request_kind",
                "thread":  "client_metadata.thread_id",
                "missing": "client_metadata.nope.nothing" } }
            """), Forward("""{"usage":{"input_tokens":5}}"""));

        var row = Assert.Single(store.Rows);
        Assert.NotNull(row.Metadata);
        Assert.Equal("system", row.Metadata["source"]);
        Assert.Equal("turn", row.Metadata["kind"]);
        Assert.Equal("019fc46d", row.Metadata["thread"]);
        Assert.DoesNotContain("missing", row.Metadata.Keys);
    }

    [Fact]
    public async Task NoMetadataFieldsConfigured_LeavesTheKeyOffTheRow()
    {
        var (ctx, _, store) = NewContext("""{"model":"m","input":[{"role":"user","content":"hi"}]}""");

        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(OpenAi),
            Forward("""{"usage":{"prompt_tokens":1,"completion_tokens":1}}"""));

        Assert.Null(Assert.Single(store.Rows).Metadata);
    }

    [Fact]
    public async Task OpenAiUsage_LandsInSeparateColumns_AndBodyStillReachesClient()
    {
        var (ctx, sink, store) = NewContext();

        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(OpenAi),
            Forward("""{"choices":[],"usage":{"prompt_tokens":40,"completion_tokens":110}}"""));

        var row = Assert.Single(store.Rows);
        Assert.Equal(40, row.InputTokens);
        Assert.Equal(110, row.OutputTokens);
        Assert.Equal("route-a", row.Route);
        Assert.False(row.Streamed);
        Assert.Contains("completion_tokens", Encoding.UTF8.GetString(sink.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CacheTokens_StaySeparateFromInput()
    {
        var (ctx, _, store) = NewContext();

        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(Anthropic),
            Forward("""
                {"usage":{"input_tokens":12,"output_tokens":300,
                          "cache_creation_input_tokens":9000,"cache_read_input_tokens":54000}}
                """));

        var row = Assert.Single(store.Rows);
        Assert.Equal(12, row.InputTokens);
        Assert.Equal(300, row.OutputTokens);
        Assert.Equal(9000, row.CacheWriteTokens);
        Assert.Equal(54000, row.CacheReadTokens);
    }

    [Fact]
    public async Task StreamCarryingNoUsage_IsMarkedStreamedWithZeros_RatherThanMissing()
    {
        // An error or aborted stream has frames but no totals. The row still has to record that the
        // call happened and that its cost is unknown, rather than reading as a free normal reply.
        var (ctx, _, store) = NewContext();

        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(Anthropic),
            ForwardStreaming("""
                event: error
                data: {"type":"error","error":{"message":"overloaded"}}

                """));

        var row = Assert.Single(store.Rows);
        Assert.True(row.Streamed);
        Assert.Equal(0, row.InputTokens);
        Assert.Equal(0, row.OutputTokens);
    }

    [Fact]
    public async Task NonJsonResponse_RecordsZero_WithoutThrowing()
    {
        var (ctx, _, store) = NewContext();

        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(OpenAi), Forward("502 Bad Gateway"));

        var row = Assert.Single(store.Rows);
        Assert.Equal(0, row.InputTokens);
        Assert.Equal(0, row.OutputTokens);
    }

    [Fact]
    public async Task RequestBody_YieldsModelTurnIndexAndStableSessionId()
    {
        const string turnOne = """
            {"model":"claude-opus-4-6","messages":[{"role":"user","content":"design the schema"}]}
            """;
        const string turnThree = """
            {"model":"claude-opus-4-6","messages":[
              {"role":"user","content":"design the schema"},
              {"role":"assistant","content":"here it is"},
              {"role":"user","content":"continue"}]}
            """;

        var (first, _, storeA) = NewContext(turnOne);
        await new TokenSpendPlugin().ExecuteAsync(first, Config(OpenAi), Forward("{}"));

        var (later, _, storeB) = NewContext(turnThree);
        await new TokenSpendPlugin().ExecuteAsync(later, Config(OpenAi), Forward("{}"));

        var a = Assert.Single(storeA.Rows);
        var b = Assert.Single(storeB.Rows);

        Assert.Equal("claude-opus-4-6", a.Model);
        Assert.Equal(1, a.TurnIndex);
        Assert.Equal(3, b.TurnIndex);
        Assert.Equal(a.SessionId, b.SessionId);
        Assert.NotEqual("", a.SessionId);
    }

    [Fact]
    public async Task PromptPrefix_IsOffByDefault_AndTruncatedWhenEnabled()
    {
        var longPrompt = new string('x', 500);
        var body = $$"""{"model":"m","messages":[{"role":"user","content":"{{longPrompt}}"}]}""";

        var (off, _, storeOff) = NewContext(body);
        await new TokenSpendPlugin().ExecuteAsync(off, Config(OpenAi), Forward("{}"));
        Assert.Null(Assert.Single(storeOff.Rows).PromptPrefix);

        var (on, _, storeOn) = NewContext(body);
        await new TokenSpendPlugin().ExecuteAsync(on, Config("""
            { "inputFields": ["usage.prompt_tokens"], "outputFields": ["usage.completion_tokens"],
              "capturePrompts": true, "maxPromptChars": 32 }
            """), Forward("{}"));
        Assert.Equal(32, Assert.Single(storeOn.Rows).PromptPrefix!.Length);
    }

    [Fact]
    public async Task ToolTraffic_IsCountedInBothWireFormats()
    {
        const string openAiShape = """
            {"model":"gpt-x","messages":[
              {"role":"assistant","tool_calls":[{"id":"1"},{"id":"2"}]},
              {"role":"tool","content":"result"}]}
            """;
        const string anthropicShape = """
            {"model":"claude-x","messages":[
              {"role":"assistant","content":[{"type":"tool_use","id":"1"}]},
              {"role":"user","content":[{"type":"tool_result","content":"ok"},{"type":"text","text":"go on"}]}]}
            """;

        var (a, _, storeA) = NewContext(openAiShape);
        await new TokenSpendPlugin().ExecuteAsync(a, Config(OpenAi), Forward("{}"));
        Assert.Equal(3, Assert.Single(storeA.Rows).ToolUseCount);

        var (b, _, storeB) = NewContext(anthropicShape);
        await new TokenSpendPlugin().ExecuteAsync(b, Config(OpenAi), Forward("{}"));
        Assert.Equal(2, Assert.Single(storeB.Rows).ToolUseCount);
    }

    [Fact]
    public async Task CallerComesFromTheConfiguredHeader()
    {
        var (ctx, _, store) = NewContext();
        ctx.Request.Headers["x-api-key"] = "sk-secret";

        await new TokenSpendPlugin().ExecuteAsync(ctx, Config("""
            { "inputFields": ["usage.prompt_tokens"], "outputFields": ["usage.completion_tokens"],
              "keyHeader": "x-api-key" }
            """), Forward("{}"));

        Assert.Equal("sk-secret", Assert.Single(store.Rows).Caller);
    }

    private static string Jwt(string payloadJson) =>
        "header." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";

    [Fact]
    public async Task CallerFallsBackToTheJwtClaim_WhenNoKeyHeaderMatches()
    {
        var config = Config("""
            { "inputFields": ["usage.prompt_tokens"], "outputFields": ["usage.completion_tokens"],
              "keyHeader": "x-api-key", "keyClaim": "sub" }
            """);

        var (withToken, _, store) = NewContext();
        withToken.Request.Headers.Authorization = "Bearer " + Jwt("""{"sub":"alice@example.com"}""");
        await new TokenSpendPlugin().ExecuteAsync(withToken, config, Forward("{}"));
        Assert.Equal("alice@example.com", Assert.Single(store.Rows).Caller);

        var (garbage, _, storeB) = NewContext();
        garbage.Request.Headers.Authorization = "Bearer not-a-jwt";
        await new TokenSpendPlugin().ExecuteAsync(garbage, config, Forward("{}"));
        Assert.Equal("global", Assert.Single(storeB.Rows).Caller);
    }

    [Fact]
    public async Task AnthropicMultiBlockContent_JoinsItsTextBlocks()
    {
        const string body = """
            {"model":"claude-x","messages":[
              {"role":"user","content":[{"type":"text","text":"first"},{"type":"text","text":"second"}]}]}
            """;

        var (ctx, _, store) = NewContext(body);
        await new TokenSpendPlugin().ExecuteAsync(ctx, Config("""
            { "inputFields": ["usage.prompt_tokens"], "outputFields": ["usage.completion_tokens"],
              "capturePrompts": true }
            """), Forward("{}"));

        Assert.Equal("first\nsecond", Assert.Single(store.Rows).PromptPrefix);
    }

    [Fact]
    public async Task ResponsesApiRequest_IsParsedFromInputRatherThanMessages()
    {
        // Shape taken from a captured Codex request: items are typed "message", content blocks are
        // "input_text", and the tool catalogue sits at the top level rather than inside an item.
        const string body = """
            {"model":"gpt-5.4-mini",
             "tools":[{"type":"function","name":"shell"},{"type":"function","name":"apply_patch"}],
             "input":[
               {"type":"message","role":"user","content":[{"type":"input_text","text":"first ask"}]},
               {"type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]},
               {"type":"function_call","name":"shell","arguments":"{}"},
               {"type":"message","role":"user","content":[{"type":"input_text","text":"second ask"}]}]}
            """;

        var (ctx, _, store) = NewContext(body);
        await new TokenSpendPlugin().ExecuteAsync(ctx, Config("""
            { "inputFields": ["usage.input_tokens"], "outputFields": ["usage.output_tokens"],
              "capturePrompts": true }
            """), Forward("""{"model":"gpt-5.6-luna","usage":{"input_tokens":10594,"output_tokens":6}}"""));

        var row = Assert.Single(store.Rows);
        Assert.Equal(4, row.TurnIndex);
        Assert.Equal("second ask", row.PromptPrefix);
        Assert.NotEqual("", row.SessionId);
        Assert.Equal(3, row.ToolUseCount);          // 2 declared tools + 1 function_call
        Assert.Equal(10594, row.InputTokens);
    }

    [Fact]
    public async Task RequestedAndServedModelAreRecordedSeparately()
    {
        // Codex asks for one model and the service reports another; both are worth keeping.
        var (ctx, _, store) = NewContext("""{"model":"gpt-5.4-mini","input":[{"role":"user","content":"hi"}]}""");

        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(OpenAi),
            Forward("""{"model":"gpt-5.6-luna","usage":{"prompt_tokens":1,"completion_tokens":2}}"""));

        var row = Assert.Single(store.Rows);
        Assert.Equal("gpt-5.4-mini", row.Model);
        Assert.Equal("gpt-5.6-luna", row.ServedModel);
    }

    // Frame shapes below are copied from captured traffic, not from documentation.
    private const string CodexSse = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_1","model":"gpt-5.6-luna","status":"in_progress"}}

        event: response.output_text.delta
        data: {"type":"response.output_text.delta","delta":"hi"}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_1","model":"gpt-5.6-luna","status":"completed","usage":{"input_tokens":14544,"input_tokens_details":{"cache_write_tokens":0,"cached_tokens":3456},"output_tokens":698,"output_tokens_details":{"reasoning_tokens":227},"total_tokens":15242}}}

        """;

    private const string ResponsesUsage = """
        { "inputFields": ["usage.input_tokens"], "outputFields": ["usage.output_tokens"],
          "cacheReadFields": ["usage.input_tokens_details.cached_tokens"],
          "cacheWriteFields": ["usage.input_tokens_details.cache_write_tokens"] }
        """;

    [Fact]
    public async Task StreamedCodexResponse_IsCountedFromTheTerminalFrame()
    {
        var (ctx, _, store) = NewContext();
        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(ResponsesUsage), ForwardStreaming(CodexSse));

        var row = Assert.Single(store.Rows);
        Assert.True(row.Streamed);
        Assert.Equal(14544, row.InputTokens);
        Assert.Equal(698, row.OutputTokens);
        Assert.Equal(3456, row.CacheReadTokens);
        Assert.Equal("gpt-5.6-luna", row.ServedModel);
    }

    [Fact]
    public async Task StreamIsDetectedFromTheBody_EvenWithoutTheContentTypeHeader()
    {
        // The live gateway recorded streamed=false on plainly SSE replies, so the body is the
        // evidence and the header only a declaration.
        var (ctx, _, store) = NewContext();
        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(ResponsesUsage), Forward(CodexSse));

        var row = Assert.Single(store.Rows);
        Assert.True(row.Streamed);
        Assert.Equal(14544, row.InputTokens);
    }

    [Fact]
    public async Task TruncatedStream_KeepsWhateverFramesCompleted()
    {
        // maxResponseBytes cuts mid-frame; the terminal totals are gone but the row must not lie.
        var cut = CodexSse[..(CodexSse.IndexOf("\"total_tokens\"", StringComparison.Ordinal) + 8)];

        var (ctx, _, store) = NewContext();
        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(ResponsesUsage), Forward(cut));

        var row = Assert.Single(store.Rows);
        Assert.True(row.Streamed);
        Assert.Equal(0, row.InputTokens);
    }

    [Fact]
    public async Task AnthropicStyleFrame_WithUsageAtTheFrameRoot_IsAlsoRead()
    {
        const string sse = """
            event: message_delta
            data: {"type":"message_delta","usage":{"input_tokens":12,"output_tokens":300}}

            """;

        var (ctx, _, store) = NewContext();
        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(Anthropic), ForwardStreaming(sse));

        var row = Assert.Single(store.Rows);
        Assert.Equal(12, row.InputTokens);
        Assert.Equal(300, row.OutputTokens);
    }

    // Each provider gets its own config, matching how the routes are set up. No route carries paths
    // for a service it never calls.
    private const string AnthropicProvider = """
        { "inputFields":      ["usage.input_tokens"],
          "outputFields":     ["usage.output_tokens"],
          "cacheReadFields":  ["usage.cache_read_input_tokens"],
          "cacheWriteFields": ["usage.cache_creation_input_tokens"] }
        """;

    private const string ResponsesProvider = """
        { "inputFields":         ["usage.input_tokens"],
          "subtractInputFields": ["usage.input_tokens_details.cached_tokens"],
          "outputFields":        ["usage.output_tokens"],
          "cacheReadFields":     ["usage.input_tokens_details.cached_tokens"],
          "cacheWriteFields":    ["usage.input_tokens_details.cache_write_tokens"] }
        """;

    [Fact]
    public async Task ResponsesProvider_SubtractsCachedTokensFromInput()
    {
        // 14544 input of which 3456 was cached, so 11088 is fresh.
        var (ctx, _, store) = NewContext();
        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(ResponsesProvider), Forward("""
            {"usage":{"input_tokens":14544,"input_tokens_details":{"cached_tokens":3456,"cache_write_tokens":0},
                      "output_tokens":698}}
            """));

        var row = Assert.Single(store.Rows);
        Assert.Equal(11088, row.InputTokens);
        Assert.Equal(3456, row.CacheReadTokens);
        Assert.Equal(698, row.OutputTokens);
        Assert.Equal(14544, row.InputTokens + row.CacheReadTokens);   // billable input
    }

    [Fact]
    public async Task AnthropicProvider_LeavesInputAlone()
    {
        // Anthropic already reports cache reads beside input, so nothing is subtracted.
        var (ctx, _, store) = NewContext();
        await new TokenSpendPlugin().ExecuteAsync(ctx, Config(AnthropicProvider), Forward("""
            {"usage":{"input_tokens":12,"output_tokens":300,
                      "cache_creation_input_tokens":9000,"cache_read_input_tokens":54000}}
            """));

        var row = Assert.Single(store.Rows);
        Assert.Equal(12, row.InputTokens);
        Assert.Equal(54000, row.CacheReadTokens);
        Assert.Equal(9000, row.CacheWriteTokens);
        Assert.Equal(54012, row.InputTokens + row.CacheReadTokens);
    }

    [Fact]
    public async Task SubtractionCannotDriveAColumnNegative()
    {
        var (ctx, _, store) = NewContext();
        await new TokenSpendPlugin().ExecuteAsync(ctx, Config("""
            { "inputFields": ["usage.input_tokens"],
              "subtractInputFields": ["usage.input_tokens_details.cached_tokens"],
              "outputFields": ["usage.output_tokens"] }
            """), Forward("""
            {"usage":{"input_tokens":10,"input_tokens_details":{"cached_tokens":500},"output_tokens":1}}
            """));

        Assert.Equal(0, Assert.Single(store.Rows).InputTokens);
    }

    [Fact]
    public void ValidateConfig_RejectsMissingUsageFields()
    {
        var plugin = new TokenSpendPlugin();
        Assert.Throws<InvalidOperationException>(() => plugin.ValidateConfig(Config("""{ "keyHeader": "x-api-key" }""")));
        plugin.ValidateConfig(Config(OpenAi));
    }
}

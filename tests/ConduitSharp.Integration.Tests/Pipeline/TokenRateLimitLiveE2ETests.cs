using System.Text;
using System.Text.Json;
using ConduitSharp.Integration.Tests.Fixtures;
using ConduitSharp.Plugin.TokenRateLimit;
using Xunit.Abstractions;

namespace ConduitSharp.Integration.Tests.Pipeline;

/// <summary>
/// Live end-to-end proof of the token rate limiter against a real OpenAI-compatible model server
/// (LM Studio / Ollama /v1 / llama.cpp server). The gateway forwards to it and the plugin meters the
/// tokens the model reports. Soft-skips when no server is reachable, so it is safe in CI; run it
/// locally with a server up, overriding the URL with LLM_E2E_URL if it is not on 127.0.0.1:1234.
/// </summary>
[Trait("Category", "E2E")]
public sealed class TokenRateLimitLiveE2ETests
{
    private readonly ITestOutputHelper _out;
    public TokenRateLimitLiveE2ETests(ITestOutputHelper output) => _out = output;

    private static string BaseUrl => Environment.GetEnvironmentVariable("LLM_E2E_URL") ?? "http://127.0.0.1:1234";

    private static string Routes(string upstream) => $$"""
    {
      "routes": [{
        "id": "llm",
        "route": { "match": { "path": "/{**rest}" } },
        "cluster": {
          "loadBalancingPolicy": "RoundRobin",
          "destinations": { "node-0": { "address": "{{upstream}}" } },
          "httpRequest": { "activityTimeout": "00:02:00" }
        },
        "plugins": [{
          "name": "custom", "variant": "token-rate-limit", "order": 1,
          "config": {
            "maxTokensPerWindow": 30,
            "windowSeconds": 3600,
            "keyHeader": "X-Api-Key",
            "usageFields": ["usage.prompt_tokens", "usage.completion_tokens"],
            "maxResponseBytes": 1048576
          }
        }]
      }]
    }
    """;

    [Fact]
    public async Task Meters_real_model_tokens_and_429s_over_budget()
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        string? model = await FirstChatModelOrNull(probe);
        if (model is null)
        {
            _out.WriteLine($"No OpenAI-compatible server at {BaseUrl}; skipping live token E2E.");
            return;
        }
        _out.WriteLine($"Using model {model} at {BaseUrl}");

        // GatewayFactory needs a FakeUpstream instance; the route points at the real server instead,
        // so this one just satisfies the signature and is never hit.
        await using var unused = await FakeUpstream.StartAsync();
        await using var factory = await GatewayFactory.CreateAsync(
            unused, Routes(BaseUrl),
            plugins: [new TokenRateLimitPlugin()],
            upstreamHttpTimeout: TimeSpan.FromSeconds(120));
        using var client = factory.CreateClient();

        async Task<(int Status, string Body, bool HasRetryAfter)> Ask(string apiKey)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = new StringContent(
                    $$"""{ "model": "{{model}}", "messages": [{ "role": "user", "content": "count to five" }], "max_tokens": 30 }""",
                    Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Api-Key", apiKey);
            using var resp = await client.SendAsync(req);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(), resp.Headers.Contains("Retry-After"));
        }

        // First call: forwarded to the model, 200 with a usage block, and its tokens get charged.
        var first = await Ask("alice");
        Assert.Equal(200, first.Status);
        Assert.Contains("usage", first.Body);
        _out.WriteLine("alice #1: 200, usage present");

        // Charge-after overshoots, so a couple succeed before the window is seen over budget.
        var got429 = false;
        for (var i = 2; i <= 8 && !got429; i++)
        {
            var r = await Ask("alice");
            _out.WriteLine($"alice #{i}: {r.Status}");
            if (r.Status == 429)
            {
                got429 = true;
                Assert.True(r.HasRetryAfter, "429 must carry Retry-After");
            }
        }
        Assert.True(got429, "expected a 429 once alice's token budget was exhausted");

        // A different caller has its own budget and is unaffected.
        var bob = await Ask("bob");
        _out.WriteLine($"bob #1: {bob.Status}");
        Assert.Equal(200, bob.Status);
    }

    private static async Task<string?> FirstChatModelOrNull(HttpClient probe)
    {
        try
        {
            using var resp = await probe.GetAsync($"{BaseUrl}/v1/models");
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var id = m.GetProperty("id").GetString();
                if (id is not null && !id.Contains("embed", StringComparison.OrdinalIgnoreCase))
                    return id; // first non-embedding model
            }
            return null;
        }
        catch
        {
            return null; // server not reachable
        }
    }
}

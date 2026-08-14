# tests/ConduitSharp.Integration.Tests/Pipeline/TokenRateLimitLiveE2ETests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## TokenRateLimitLiveE2ETests.Meters_real_model_tokens_and_429s_over_budget

- (was line 47) REQUIRES a running OpenAI-compatible server with a model loaded to do anything. Locally that means LM Studio started with a model loaded (Developer tab > Start Server, default http://127.0.0.1:1234), or Ollama / llama.cpp server via LLM_E2E_URL. With no server reachable this SOFT-SKIPS: it returns without asserting, so it passes in CI (where no model runs) instead of failing. So a green run here does not prove the path unless a server was up — check the test output for "Using model ..." versus "No OpenAI-compatible server ... skipping".
- (was line 60) Soft skip: no LM Studio / OpenAI-compatible server up. Pass without exercising anything.
- (was line 66) GatewayFactory needs a FakeUpstream instance; the route points at the real server instead, so this one just satisfies the signature and is never hit.
- (was line 88) First call: forwarded to the model, 200 with a usage block, and its tokens get charged.
- (was line 94) Charge-after overshoots, so a couple succeed before the window is seen over budget.
- (was line 108) A different caller has its own budget and is unaffected.

## TokenRateLimitLiveE2ETests.FirstChatModelOrNull

- (was line 125) first non-embedding model
- (was line 131) server not reachable

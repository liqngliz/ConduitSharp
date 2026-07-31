# plugins/ConduitSharp.Plugin.TokenRateLimit/tests/ConduitSharp.Plugin.TokenRateLimit.Tests/TokenRateLimitPluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## TokenRateLimitPluginTests.ChargesSummedUsageFields_ToTheWindowCounter

- (was line 39) 40 + 110 response passed through

## TokenRateLimitPluginTests.SecondRequest_OverBudget_Returns429_WithRetryAfter

- (was line 47) window already at budget

## TokenRateLimitPluginTests.WorksAcrossProviders_ByConfiguredFields

- (was line 85) Gemini Ollama native

## TokenRateLimitPluginTests.NonJsonBody_ChargesNothing

- (was line 103) An SSE stream is not a single JSON document.
- (was line 107) still streamed to the client

## TokenRateLimitPluginTests.CaptureMemoryBytes_IsMaxResponseBytes

- (was line 127) default

## TokenRateLimitPluginTests.ValidateConfig_Rejects_BadConfig

- (was line 131) no maxTokens empty fields bad window

## TokenRateLimitPluginTests.FakeJwt

- (was line 143) Builds a structurally valid (unsigned) JWT: base64url(header).base64url(payload).sig

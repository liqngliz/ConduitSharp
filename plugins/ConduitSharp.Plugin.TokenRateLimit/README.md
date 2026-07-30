# ConduitSharp.Plugin.TokenRateLimit

Rate limits by **LLM tokens**, not request count. Meters the token usage a model reports and charges
it against a per-caller, fixed-window budget, reusing the gateway's shared `IRateLimitStore` (in
memory, or the [Redis drop-in](../ConduitSharp.RateLimit.RedisProtocol) for a budget shared across
replicas).

Plugin variant: `token-rate-limit`.

## Why it is different from `rate-limit`

A request's token cost is unknown until the model answers, so this cannot check-then-allow. It is
**charge-after**: it reads the token counts out of the response body, adds them to the window's
counter, and the *next* request that finds the window already over budget gets a `429`. A single
request overshoots its budget by at most its own cost. If you need a hard cap, this is not it.

## How it works

1. **Before the forward** — if the caller's window is already at or over `maxTokensPerWindow`, return
   `429` with `Retry-After`.
2. **Forward**, buffering the response so it can be parsed. The buffer is **write-through**: the client
   still receives the response as it streams; the gateway keeps a copy to parse afterward. Bounded by
   `maxResponseBytes` and reserved against `Gateway:RequestLimits:MaxRamBufferedBodyBytes`.
3. **After the forward** — parse the buffered body as JSON, sum the configured `usageFields`, and add
   the total to the window counter.

## Config

```json
{
  "name": "custom", "variant": "token-rate-limit", "order": 3,
  "config": {
    "maxTokensPerWindow": 100000,
    "windowSeconds": 60,
    "keyClaim": "sub",
    "usageFields": ["usage.prompt_tokens", "usage.completion_tokens"],
    "maxResponseBytes": 1048576
  }
}
```

| Setting | Required | Default | Meaning |
| :--- | :--- | :--- | :--- |
| `maxTokensPerWindow` | yes | — | token budget per caller per window |
| `windowSeconds` | no | `60` | fixed window length |
| `keyHeader` | no | — | header whose value keys the budget (e.g. `X-Api-Key`) |
| `keyClaim` | no | — | JWT claim keying the budget (e.g. `sub`), read from the `Authorization` Bearer token. Used when `keyHeader` is absent. The token is **not** re-validated — put `jwt-auth` earlier in the chain. |
| `usageFields` | yes | — | dotted JSON paths in the response summed as the cost |
| `maxResponseBytes` | no | `1048576` | cap on response bytes buffered to find the usage |

With neither `keyHeader` nor `keyClaim`, the budget is a single per-route counter shared by all callers.

## `usageFields` per provider

The response field names differ by provider. One plugin covers all — config picks the fields:

| Provider | `usageFields` |
| :--- | :--- |
| OpenAI (and OpenAI-compatible: LM Studio, Ollama `/v1`) | `["usage.prompt_tokens", "usage.completion_tokens"]` or `["usage.total_tokens"]` |
| Anthropic | `["usage.input_tokens", "usage.output_tokens"]` |
| Gemini | `["usageMetadata.totalTokenCount"]` |
| Ollama native (`/api/chat`) | `["prompt_eval_count", "eval_count"]` |

## Streaming caveat

Usage lives in the body, so a **non-streaming** JSON response parses cleanly and is metered. A
**streaming** response (SSE / NDJSON) is not a single JSON document, so it parses as 0 tokens and the
call goes **uncounted**. Front the model's non-streaming endpoint, or extend the plugin with a
per-provider SSE reader (OpenAI's usage is in the final chunk only when `stream_options.include_usage`
is set; Anthropic spreads it across `message_start` and `message_delta`).

## Ordering

Put `token-rate-limit` after `jwt-auth` (so `keyClaim` sees a validated token) and after any
`body-capture`. It reads the response on the way back out, so its `order` relative to other
response-touching plugins follows the reverse-unwind rule (see the gateway docs on plugin ordering).

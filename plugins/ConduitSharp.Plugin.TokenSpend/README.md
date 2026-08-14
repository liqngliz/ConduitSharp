# ConduitSharp.Plugin.TokenSpend

Records what every LLM call cost, as durable history. It reads the same provider `usage` block
[`token-rate-limit`](../ConduitSharp.Plugin.TokenRateLimit) reads, but instead of charging a window
it writes one row per request to an `ISpendStore`, so the data is still there weeks later.

Plugin variant: `token-spend`.

The dashboard that reads this history is a separate piece of work; the design lives in
`docs/planning/SPEND.MD`. This package is capture and storage only.

## Why the counts are not summed

Input, output, cache-write, and cache-read are four separate fields on the row, because they price
differently: a cache write runs about 1.25x input and a cache read about a tenth. Summing them into
one number hides the largest lever a caller has over their bill.

## How it works

1. **Read the request** — the model name, message count, tool blocks, and the newest user message
   come from the request body, not the response. An Anthropic or OpenAI request resends the whole
   message array every turn, so one intercepted request yields the turn index and a stable session
   id with no cooperation from the client.
2. **Forward**, buffering the response **write-through**: the client receives bytes as they arrive
   while the gateway keeps a bounded copy to parse. Capped by `maxResponseBytes` and reserved
   against `Gateway:RequestLimits:MaxRamBufferedBodyBytes`.
3. **Write the row** — parse the four field groups in one pass and queue a `SpendRecord`. Queuing is
   non-blocking; a background task does the disk write, so a slow disk costs the request nothing.

## Config

```json
{
  "name": "custom", "variant": "token-spend", "order": 4,
  "config": {
    "inputFields":  ["usage.input_tokens"],
    "outputFields": ["usage.output_tokens"],
    "cacheWriteFields": ["usage.cache_creation_input_tokens"],
    "cacheReadFields":  ["usage.cache_read_input_tokens"],
    "thinkingFields":   ["usage.output_tokens_details.thinking_tokens"],
    "keyHeader": "x-api-key"
  }
}
```

| Setting | Required | Default | Meaning |
| :--- | :--- | :--- | :--- |
| `inputFields` | yes | — | dotted response paths summed into the input column |
| `outputFields` | yes | — | dotted response paths summed into the output column |
| `cacheWriteFields` | no | `[]` | cache-creation tokens, priced above input |
| `cacheReadFields` | no | `[]` | cache-read tokens, priced far below input |
| `thinkingFields` | no | `[]` | reasoning tokens. On the wire, a **subset of `out`** (the dashboard splits them into separate Out and Think metrics). Anthropic `usage.output_tokens_details.thinking_tokens`, Responses API `…reasoning_tokens`, Chat Completions `usage.completion_tokens_details.reasoning_tokens`. `0` also means "provider reports no such field", not "model did not think". |
| `keyHeader` | no | — | header identifying the caller (e.g. `x-api-key`). Only its salted hash is stored. |
| `keyClaim` | no | — | JWT claim identifying the caller, used when `keyHeader` is absent. Not re-validated: put `jwt-auth` earlier in the chain. |
| `maxResponseBytes` | no | `1048576` | cap on response bytes buffered to find the usage |
| `maxRequestBytes` | no | `1048576` | cap on request bytes parsed for model and messages |
| `capturePrompts` | no | `false` | store a bounded prefix of the newest user message |
| `maxPromptChars` | no | `200` | how much of that message is kept |
| `sessionField` | no | — | dotted request path holding the client's own session id. Descends into a JSON document held in a string, which is how Claude Code and Codex ship theirs. Name the leaf, not its parent: Claude's `user_id` object also holds a device id and an account uuid. Unset falls back to hashing the first user message, which splits a session on compaction and merges sessions sharing a synthetic preamble. |
| `metadataFields` | no | `{}` | named dotted request paths copied onto each row under `meta`, e.g. `{"source": "client_metadata.x-codex-turn-metadata.thread_source"}`. Values truncated to 200 characters. Nothing is captured unless a path names it, so point these at metadata rather than at content. |

### Fields per provider

| Provider | input / output | cache |
| :--- | :--- | :--- |
| OpenAI and OpenAI-compatible (LM Studio, Ollama `/v1`) | `usage.prompt_tokens` / `usage.completion_tokens` | — |
| Anthropic | `usage.input_tokens` / `usage.output_tokens` | `usage.cache_creation_input_tokens`, `usage.cache_read_input_tokens` |
| Ollama native (`/api/chat`) | `prompt_eval_count` / `eval_count` | — |

## Row

| Field | Type | Meaning |
| :--- | :--- | :--- |
| `ts` | string | completion time, UTC. Also selects the day file. |
| `route` | string | route that served the call, from `ConduitSharp.RouteId` |
| `model` | string | model the caller asked for, from the request body |
| `servedModel` | string | model the provider says served it, from the response. Empty on every streamed reply. |
| `caller` | string | salted hash of the API key or JWT claim, never the raw credential |
| `trace` | string | W3C trace id, or `HttpContext.TraceIdentifier` when no tracer is listening |
| `in` / `out` | number | input and output tokens |
| `cacheWrite` / `cacheRead` | number | cache-creation and cache-read tokens |
| `think` | number | reasoning tokens, a subset of `out` |
| `session` | string | client's session id, or a hash of the first user message |
| `turn` | number | messages the request carried |
| `tools` | number | tool-use blocks in the request |
| `ms` | number | upstream wall-clock milliseconds |
| `streamed` | bool | response was an event stream, decided from the body not the header |
| `prompt` | string? | prefix of the newest user message, only when `capturePrompts` is on |
| `sessionName` | string? | prefix of the session's first user message, used as a title |
| `meta` | object? | values named by `metadataFields` |

### Joining to the wire log

`trace` is `Activity.Current?.TraceId ?? HttpContext.TraceIdentifier`, the same expression and the
same fallback [`body-capture-file`](../ConduitSharp.Plugin.BodyCaptureToFile) writes, so a row joins
to the bodies it was counted from and to its spans. Timestamp plus route cannot: two concurrent
calls on one route share both.

With no tracer listening the value is a connection-scoped identifier (`0HN7...:00000001`) rather
than a W3C id. Both sides fall back identically, so the join still holds.

## Storage

`JsonlSpendStore` writes one JSON object per line, one file per UTC day
(`spend-2026-07-31.jsonl`), under `~/.conduit-spend` by default (`CONDUIT_SPEND_DATA` overrides).
Day files are never rewritten and never deleted: a spend log that rolls away its own history is
useless, which is the one place this diverges from the otherwise identical sink in
[`body-capture-file`](../ConduitSharp.Plugin.BodyCaptureToFile).

`ISpendStore` is the seam. A SQLite or Postgres backend implements the same three methods and is
registered in place of the JSONL one, the same way `IRateLimitStore` and `ICacheService` swap.

## Privacy

Token counts, model names, and a salted hash of the caller by default. Never body content unless
`capturePrompts` is on, and then only a bounded prefix of the newest user message.

The caller header on a provider route is `x-api-key` or `Authorization`, which carries the user's
real API key. It is hashed with HMAC-SHA256 under a 32-byte salt generated once per data directory,
truncated to 96 bits, and only that hash is written. The raw credential never reaches disk.
Nothing leaves the machine at all.

## Streaming is recorded, not counted

An SSE response is not one JSON document, so this build does not parse it. A `text/event-stream`
reply is written with `"streamed": true` and zero token counts, which keeps the call visible in the
history as explicitly uncounted rather than silently absent.

This matters because **Claude Code streams by default**. Front a non-streaming endpoint to measure
it, or extend the plugin: Anthropic spreads usage across `message_start` and `message_delta`, while
OpenAI emits it in a final chunk only when the client sets `stream_options.include_usage`.

## Ordering

Put `token-spend` after `jwt-auth` (so `keyClaim` sees a validated token). It reads the response on
the way back out, so its `order` relative to other response-touching plugins follows the
reverse-unwind rule described in the gateway docs on plugin ordering.

## Registering the store

The plugin resolves `ISpendStore` from request services, so the host registers it once:

```csharp
builder.Services.AddSingleton<ISpendStore>(new JsonlSpendStore());
builder.Services.AddSingleton<IPipelinePlugin, TokenSpendPlugin>();
```

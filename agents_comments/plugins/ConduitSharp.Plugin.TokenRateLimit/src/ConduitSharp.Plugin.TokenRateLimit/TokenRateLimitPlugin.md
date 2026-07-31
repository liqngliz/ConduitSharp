# plugins/ConduitSharp.Plugin.TokenRateLimit/src/ConduitSharp.Plugin.TokenRateLimit/TokenRateLimitPlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## TokenRateLimitPlugin.ExecuteAsync

- (was line 68) The store may be shared across routes and replicas, so the counter key carries the route id.
- (was line 74) Charge-after: deny once the window is already at or over budget.
- (was line 84) using, not a manual return at the end: an exception out of next() would otherwise drop the rented buffer on the floor instead of handing it back to the pool.

## TokenRateLimitPlugin.ClaimFromBearer

- (was line 117) Reads a claim out of the Authorization Bearer token WITHOUT validating it — an upstream jwt-auth plugin has already validated the signature; this only needs the value as a per-caller key.

## TokenRateLimitPlugin.ExtractTokens

- (was line 150) Parses the buffered body as JSON and sums the configured dotted-path fields. Returns 0 when the body is not JSON (e.g. an SSE stream) or holds none of the fields.

## TokenRateLimitPlugin

- Charge-after rather than check-then-allow because a call's cost is unknown until the model answers. If you need a hard cap, this is not it.
- The response is buffered so the usage fields can be parsed, write-through so the client still receives it as it streams, bounded by maxResponseBytes and reserved against the RAM budget through CaptureMemoryBytes.
- usageFields per provider: OpenAI usage.prompt_tokens + usage.completion_tokens; Anthropic usage.input_tokens + usage.output_tokens; Gemini usageMetadata.totalTokenCount; Ollama prompt_eval_count + eval_count. The listed fields are summed.
- SSE goes uncounted because it is not one JSON document. Front the model's non-streaming endpoint, or add a per-provider SSE reader.

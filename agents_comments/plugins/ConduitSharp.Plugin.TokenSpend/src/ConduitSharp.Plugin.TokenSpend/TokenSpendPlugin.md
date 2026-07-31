# plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/TokenSpendPlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## TokenSpendPlugin.ExecuteAsync

- (was line 79) using, not a manual return at the end: an exception out of next() would otherwise drop the rented buffer on the floor instead of handing it back to the pool.
- (was line 108) Parsing an SSE body as one JSON document yields nothing, so skip it and let Streamed carry the meaning. Anything else gets its four counts read in a single pass.

## TokenSpendPlugin.WithUsage

- (was line 120) Response side
- (was line 141) Not a JSON body at all (an error page, a truncated capture). The row still records that the call happened, with zero counts.

## TokenSpendPlugin.TryGetByPath

- (was line 156) Lifted from TokenRateLimitPlugin: the same dotted-path read over the same provider bodies. ponytail: ten lines, still duplicated. The write-through tee those two plugins also shared is now BoundedTeeStream in Core; this is small enough that hoisting it would cost the Core package a public helper for less than it saves. Hoist it if a third plugin needs it.

## TokenSpendPlugin.ReadRequestAsync

- (was line 173) Request side
- (was line 178) The gateway buffers the body because this plugin declares ReadsRequestBody, so it is seekable by now. A GET or a non-buffered body simply yields no facts.
- (was line 187) ReadAtLeastAsync, not ReadAsync: a single read can return short while data remains, which would truncate the parse and silently lose the message array.

## TokenSpendPlugin.ParseRequest

- (was line 248) Every turn resends the whole conversation, so the opening message is stable for the life of a session and needs no client-supplied session header.

## TokenSpendPlugin.ToolBlocks

- (was line 262) Counts the tool traffic in one message across both wire formats: OpenAI puts it in a "tool" role or a tool_calls array, Anthropic in content blocks typed tool_use / tool_result.

## TokenSpendPlugin.TextOf

- (was line 285) "content" is a bare string on OpenAI and an array of typed blocks on Anthropic.

## TokenSpendPlugin.ResolveClientKey

- (was line 314) Caller identity

## TokenSpendPlugin.ClaimFromBearer

- (was line 331) Reads a claim out of the Authorization Bearer token WITHOUT validating it — an upstream jwt-auth plugin has already checked the signature; this only needs a per-caller value.

## TokenSpendPlugin

- Reads the same provider usage block token-rate-limit reads, but persists a row instead of charging a window, so the data is still there weeks later to answer which habits burn tokens.
- The four token counts stay separate because they price differently: a cache write runs about 1.25x input and a cache read about a tenth. One total would hide the biggest lever a caller has.
- The response is buffered write-through, so the client receives bytes as they arrive while the gateway keeps a bounded copy to parse.
- ReadsRequestBody is true because the request body is what supplies the model, the turn index and the session grouping: an Anthropic or OpenAI request carries the whole message array every turn, so one intercepted request yields all three without the client cooperating.
- Recording SSE as streamed with zero tokens is deliberate: the call stays visible in the history as uncounted rather than missing from it. Claude Code streams by default, so front a non-streaming endpoint to measure it.

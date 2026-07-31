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

## TokenSpendPlugin.ParseRequest (Responses API)

- Falls back from "messages" to "input" because the Responses API, which Codex speaks, names the conversation array differently. Same role/content shape, so the rest of the walk is unchanged. Verified against a captured Codex request rather than documentation.
- Content blocks there are typed input_text / output_text but still carry a "text" field, which is why TextOf works untouched.

## TokenSpendPlugin.ToolBlocks

- Responses declares the tool catalogue at the top level of the request and represents an invocation as an item typed function_call / function_call_output, where Chat Completions uses a tool_calls array and a "tool" role. Both are counted so the column means the same thing across wire formats.

## TokenSpendPlugin.WithUsage (ServedModel)

- Model is recorded twice on purpose. Model is what the caller asked for, read from the request; ServedModel is what the provider says it used, read from the response. Live Codex traffic showed a request for gpt-5.4-mini answered by gpt-5.6-luna, so collapsing them would hide a real substitution.
- ServedModel is empty for streamed replies until SSE parsing lands, because the name only appears inside the event frames.

## TokenSpendPlugin.WithStreamedUsageAsync

- Use System.Net.ServerSentEvents.SseParser. It is in the shared framework on net10.0, verified by compile check, so this needs no package and no hand-rolled data: line scanner.
- The usage lives in the terminal frame. A captured Codex response.completed event carried {"input_tokens":10594,"input_tokens_details":{"cache_write_tokens":0,"cached_tokens":3456},"output_tokens":6,"total_tokens":10600}.
- Note the semantics differ from Anthropic: cached_tokens is nested inside input_tokens_details and is a SUBSET of input_tokens, whereas Anthropic reports cache reads alongside input_tokens. Summing them the same way would double-count on OpenAI.

## TokenSpendPlugin.LooksLikeEventStream

- Detection reads the body, not just the Content-Type. Live traffic recorded streamed=false on replies that plainly began "event: response.created", and a row claiming a free normal call is worse than one admitting it went uncounted. A YARP probe showed Response.ContentType does carry text/event-stream after the forward, so the header is not generally missing; the body is simply the evidence rather than the declaration.

## TokenSpendPlugin.WithStreamedUsageAsync (the trailing blank line)

- Two newlines are appended before parsing. SseParser only dispatches an event once it sees the blank line terminating it, so a capture bounded by maxResponseBytes drops its last frame, which is exactly the one holding the totals. Measured: the same stream yields [a] without the terminator and [a, response.completed] with it.
- Every frame is tried and the last with a non-zero total wins, because providers put running counts in intermediate frames and the final tally in the terminal one.
- Paths are applied at the frame root and again under a "response" wrapper: Anthropic puts usage at the top of a message_delta frame, OpenAI nests it one level deeper inside response.completed. Both shapes are covered by tests copied from captured traffic.


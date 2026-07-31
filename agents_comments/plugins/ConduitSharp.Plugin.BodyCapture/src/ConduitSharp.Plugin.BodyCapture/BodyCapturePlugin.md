# plugins/ConduitSharp.Plugin.BodyCapture/src/ConduitSharp.Plugin.BodyCapture/BodyCapturePlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## BodyCapturePlugin

- (was line 66) Reuse-branch and response records log here; the streaming request tee re-homes HttpLogging's records to the same category (see BodyCaptureLoggerFactory), so every path surfaces under one name.

## BodyCapturePlugin..ctor

- (was line 76) Read as long so an oversized value reports what it is rather than failing as an opaque int conversion error. HttpLogging's RequestBodyLogLimit is an int, so that is the hard cap.
- (was line 87) HttpLoggingMiddleware is internal, reachable only via UseHttpLogging() on a pipeline we own, and it resolves its options from DI. The gateway hands drop-in plugins no seam onto the host's IServiceCollection, so the middleware's options live in a container of our own — wired to the host's ILoggerFactory, which is what keeps the captured bodies flowing out through the host's OpenTelemetry logger provider rather than into a private void.
- (was line 93) Request body only: the response is captured by our own write-through tee, which also covers the retry/reuse routes HttpLogging never sees.
- (was line 97) TryAdds ILoggerFactory, so the line above wins
- (was line 105) Built once — the plugin is a singleton and the middleware is stateless per request. The terminal hands control back to the gateway's chain through Items, the same handoff the gateway itself uses for YARP's forward (GatewayItems.ProxyNext).

## BodyCapturePlugin.ReadsRequestBody

- (was line 120) Deliberately false: we do NOT need the gateway's buffered, rewindable body. HttpLogging observes the request bytes as YARP streams them, and the response tee observes them as YARP writes them.

## BodyCapturePlugin.ValidateConfig

- (was line 135) Catch the pre-2.0 flat shape ({ "maxSize": N } at the top level, request-only). It no longer captures anything, so fail the load with a migration hint instead of a silent no-op.

## BodyCapturePlugin.ExecuteAsync

- (was line 172) Response capture: swap Response.Body for a bounded write-through tee before the forward runs (the forward is inside next()), then log the captured prefix once it returns. Works on both request branches, so retry/reuse routes capture the response too.

## BodyCapturePlugin.CaptureFromBufferedBodyAsync

- (was line 211) Reads up to maxSize off the already-buffered, seekable request body, rewinds, logs the prefix, forwards. Position is left at 0 for the forward and any retry replay. No EnableBuffering — the gateway's buffer is reused in place, staying under its budget.

## BodyCapturePlugin.DirectionMaxSize

- (was line 265) The direction's effective maxSize, or 0 when it is not captured. Clamped to the ceiling as well as validated, so the bound holds even if a host ever skips ValidateConfig. Block absent or maxSize <= 0 means "do not capture this direction"; block present without maxSize means the default.

## BodyCapturePlugin

- Request capture picks the cheaper path per request. When the gateway already buffered the body (a retry route, or a body-reading plugin on the same route) HttpRequest.Body is seekable and the prefix is read straight off it, raw, so binary is captured too. Otherwise the route is streaming and HttpLogging tees the first maxSize bytes as YARP streams them upstream, text-ish media types only.
- Response capture wraps HttpResponse.Body in a bounded write-through tee, so it runs on both request paths: a retry route captures the response too, and raw bytes mean binary responses are captured.
- Captured bodies are emitted through the host's ILoggerFactory under this plugin's category, which is what BodyCaptureLoggerFactory re-homes them for.

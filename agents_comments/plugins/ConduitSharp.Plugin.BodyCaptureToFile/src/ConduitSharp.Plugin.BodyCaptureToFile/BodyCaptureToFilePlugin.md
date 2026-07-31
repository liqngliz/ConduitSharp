# plugins/ConduitSharp.Plugin.BodyCaptureToFile/src/ConduitSharp.Plugin.BodyCaptureToFile/BodyCaptureToFilePlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## BodyCaptureToFilePlugin.ReadsRequestBody

- (was line 34) Static, so it cannot vary by config: the plugin always declares it needs the buffered request body. Request capture reads that buffer directly. A response-only route therefore still buffers the request even though it captures nothing from it — the cost of a per-plugin (not per-config) property. Response capture itself needs no buffering; it tees Response.Body.

## BodyCaptureToFilePlugin

- (was line 41) ponytail: single sink path for the plugin instance. The plugin is a shared singleton, so if two routes set different logPath the last ValidateConfig wins for all. One file per gateway is the intended layout; per-route files would need the path carried per entry through the channel.
- (was line 45) Rolled at this size, keeping one .1 backup, so the sink is bounded at ~2x. Appending forever fills a real disk (ENOSPC kills the writer) or hits the size cap on a tmpfs mount.

## BodyCaptureToFilePlugin..ctor

- (was line 52) logger is optional so the plugin still constructs where nothing registers one (the tests do exactly that); the gateway's DI supplies it in a real host.
- (was line 58) DropWrite, not DropOldest: DropOldest silently evicts the queued entry whose byte[] is rented from ArrayPool, and there is no drop callback to return it — that leaks pooled buffers under exactly the backpressure this plugin is benchmarked at. DropWrite makes the full-channel case fall to TryWrite==false below, where the buffer IS returned.
- (was line 67) The queue can hold this much pooled RAM beyond what any in-flight request has reserved (see CaptureMemoryBytes). Stated once at startup so it is a known number rather than one discovered from a memory graph.

## BodyCaptureToFilePlugin.DirectionMaxSize

- (was line 95) The direction's effective maxSize, or 0 when that direction is not captured. Block absent or maxSize <= 0 means off; block present without maxSize means the default.

## BodyCaptureToFilePlugin.ExecuteAsync

- (was line 155) Response capture: bounded write-through tee, enqueued after the forward writes the body.
- (was line 172) DetachBuffer hands pool ownership to Enqueue, which returns the array if the channel is full. CapturedLength and Truncated survive the detach, so evaluating them after it in argument order is still correct.

## BodyCaptureToFilePlugin.CaptureRequestAsync

- (was line 181) Reads the request prefix off the gateway's buffered (seekable) body — this plugin declares ReadsRequestBody, so the body is buffered by the time it runs.
- (was line 188) ReadAtLeastAsync, not ReadAsync: a single read may return short on a stream that still has data, which would both cut the capture and leave `truncated` false.

## BodyCaptureToFilePlugin.Enqueue

- (was line 206) Hands a pooled buffer to the writer channel. The writer owns and returns it; on a full channel (DropWrite) TryWrite fails and we return it here so it never leaks.

## BodyCaptureToFilePlugin.ProcessQueueAsync

- (was line 243) ponytail: a maxSize cut can split a multibyte UTF-8 char at the byte boundary; GetChars emits U+FFFD for the partial tail. Fine for a debug body dump — upgrade to a Decoder if exact truncation ever matters.
- (was line 267) Return the pooled buffer even if the write throws, so an IO error can't leak it out of the pool.
- (was line 285) This task is the only writer: once it dies, capture stops for the life of the process while every request still succeeds and the plugin still looks healthy. Silence here is the same failure shape as an over-sized OTLP batch — say so loudly instead.

## BodyCaptureToFilePlugin.Roll

- (was line 293) Single backup, overwritten: bounds the sink at ~2x _maxFileBytes with no scheduler and no dependency. A log shipper (promtail, the collector's filelog receiver) follows the rename.
- (was line 305) A failed roll is not fatal — the next iteration reopens and keeps appending.

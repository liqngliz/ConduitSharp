# tests/Shared/E2E/CaptureMemoryHarness.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## CaptureStyle

- (was line 14) Real-Kestrel harness for measuring the memory cost of request-body capture. There is one capture plugin now — BodyCapturePlugin (variant "body-capture") — which picks its path per request: - route already buffered (retry / readsBody): reuse the seekable buffer, read the prefix off it - plain streaming route: tee a <=ceiling prefix via HttpLogging So a route's shape (retry on/off), not a plugin choice, decides the behaviour.
- (was line 20) Both the gateway and the upstream run as real Kestrel servers on loopback in THIS process. The client streams the body (StreamingContent never materializes it) and the upstream drains+discards it, so neither side accumulates the body — any memory the sampler sees is the gateway's capture. Single-process, so RSS carries GC noise; PeakManagedHeap + PeakSpillBytes are the sharp signals.

## MemorySampler.Start

- (was line 63) Collect so the floor excludes prior-test garbage, then measure the idle baseline to subtract.

## MemorySampler.Stop

- (was line 78) one final reading — the peak may be at the very end
- (was line 80) cancellation

## MemorySampler.Sample

- (was line 106) file rolled/deleted mid-scan
- (was line 108) dir churned

## StreamingContent..ctor

- (was line 123) Media type matters: HttpLogging (the tee) only captures bodies of text-ish media types (text/*, application/json, ...). A binary type makes the tee a no-op — so a fair tee-vs-buffer capture comparison must use a type HttpLogging will actually tee.

## DiscardUpstream.StartAsync

- (was line 170) discard

## CaptureGatewayHost.StartAsync

- (was line 222) MUST be Information-enabled: HttpLogging (the tee) skips buffering the body when its log level is disabled, which would make the tee a no-op and every measurement a lie. This provider is enabled but discards the formatted message (counting records) so the tee runs for real without printing a 500 MB body.

## CaptureGatewayHost.CaptureCountingLoggerProvider

- (was line 260) Enabled at Information (so HttpLogging actually buffers + emits), but the sink drops the message after counting it — the tee's real memory cost is the buffered body, which happens before Log(). Counts ONLY records under the capture category: the tee's HttpLogging records are re-homed there (BodyCaptureLoggerFactory) and the reuse branch logs there directly, so this excludes unrelated gateway/YARP Information logs that would otherwise inflate the count.

## CaptureGatewayHost.CaptureCountingLoggerProvider.EnabledNullLogger

- (was line 275) Enabled (so HttpLogging still buffers), but discards.

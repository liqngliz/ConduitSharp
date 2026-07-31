# tests/ConduitSharp.Integration.Tests/Fixtures/FakeOtlpCollector.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## FakeOtlpCollector.StartAsync

- (was line 40) HttpListener cannot bind port 0 — probe random high ports until one sticks.

## FakeOtlpCollector.AcceptLoopAsync

- (was line 64) listener stopped
- (was line 74) An empty body is a valid (all-accepted) Export*ServiceResponse message.

## FakeOtlpCollector

- Built on HttpListener rather than ASP.NET Core deliberately. A Kestrel-based receiver in the same process emits Microsoft.AspNetCore activities for every export POST it receives, which the gateway's process-wide instrumentation picks up and re-exports: a feedback loop that floods and stalls the exporter. HttpListener creates no activities, so receiving an export is telemetry-silent.

# tests/ConduitSharp.Integration.Tests/Fixtures/FakeUpstream.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## FakeUpstream.ReceivedRequests

- (was line 14) Guarded by _requestsLock: concurrent test requests dispatch in parallel.

## FakeUpstream.StartAsync

- (was line 29) The fake never imposes a body limit: tests exercise the gateway's limits, and Kestrel's 30 MB default here would 413 large uploads before they matter.
- (was line 36) instance is captured by reference so the closure sees the final value once assigned below, before any request can arrive.

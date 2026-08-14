# tests/ConduitSharp.Integration.Tests/Gateway/RetryBodyReplayTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RetryBodyReplayTests.Retry_AfterUpstreamFails_ReplaysTheBodyIntact

- (was line 36) Fail the first attempt so the retry has to fire, then succeed — recording what each attempt actually carried rather than only that it arrived.
- (was line 54) over the 4 KiB floor, so it is a real buffered body
- (was line 59) the retry fired at all ...and both attempts were whole

## RetryBodyReplayTests.Retry_AfterSpill_ReplaysTheBodyIntactFromDisk

- (was line 67) The same guarantee once the body has left memory. Rewinding a FileBufferingReadStream that spilled means re-reading the temp file, so this covers a different code path than the RAM case above — and it is the path a large upload actually takes.
- (was line 85) force the spill
- (was line 90) 64x the threshold — spills well before the end

## RetryBodyReplayTests.Post_OnARetryRoute_IsNeverReplayed_SoItsBodyIsNeverAtRisk

- (was line 102) The other half of the guarantee: ConduitSharp's retries are method-aware, so a POST on a retry route streams and is never replayed. That is what makes streaming it safe — an un-rewindable body is only a hazard for a request something might retry.
- (was line 113) would trigger a retry, if POST were retryable
- (was line 123) one attempt only — the 503 is surfaced, not retried and it arrived whole

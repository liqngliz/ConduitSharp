# tests/ConduitSharp.Integration.Tests/Gateway/BufferTierStepDownTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## BufferTierStepDownTests.Body

- (was line 17) ASCII so FakeUpstream can capture the body as text.

## BufferTierStepDownTests.MemoryTierDisabled_BodyStillServed_FromDisk

- (was line 29) Memory tier off entirely → every body spills from the first byte. Slower, still a 200. This is the rung that must not turn into a 503.

## BufferTierStepDownTests.MemoryTierTooSmallForThreshold_BodyStillServed_FromDisk

- (was line 49) Memory tier smaller than the 4 KiB minimum threshold → no request can claim RAM, so everything spills. Availability must be unaffected.

## BufferTierStepDownTests.BodyOutgrowsItsMemoryThreshold_SpillsMidBody_AndForwardsIntact

- (was line 69) Crosses the memory→disk boundary *during* the read, which is where the accounting is easiest to get wrong: the rented buffer goes back to the pool and the memory reservation is released while the request is still in flight.
- (was line 81) 64x the threshold → guaranteed spill mid-read

## BufferTierStepDownTests.SpilledRequests_ReleaseTheirMemoryReservation_SoLaterRequestsStillGetRam

- (was line 91) The leak that would quietly undo the whole design: if the memory reservation were not released on spill (or in the finally), the tier would drain over time and every request would end up on disk, with nothing failing loudly to say so. A tier sized for exactly one in-flight body proves reuse — sequential requests can only all succeed if each hands its RAM back.
- (was line 100) exactly one body's worth
- (was line 105) outgrows the threshold → spills → must release its RAM

## BufferTierStepDownTests.DiskBudgetExhausted_Returns503_WhenTheBodyMustSpill

- (was line 119) The last rung. A body too large for the per-request RAM threshold spills, and the disk budget is what bounds spilled bytes — exhaust it and the request sheds. RAM headroom does not rescue it: the body was already routed to disk because it could not fit the threshold.
- (was line 126) floor — a 100 KiB body cannot fit plenty of RAM, and irrelevant once it spills ...but nowhere to spill to

## BufferTierStepDownTests.SpillDirectory_WhenConfigured_IsUsedInsteadOfSystemTemp

- (was line 141) Worth pinning: in containers /tmp is often tmpfs, so "spilling" there is still RAM and the disk tier silently becomes a second memory tier. Operators need this to actually work.
- (was line 151) force the spill

## BufferTierStepDownTests.SpillDirectory_PointedAtNothing_FailsTheSpill

- (was line 172) The positive test above cannot prove the setting was honoured — the spill file is deleted on dispose, so an ignored setting would pass it just as happily by quietly using the system temp path. This is the half that actually binds: an unusable spill directory must surface, not silently fall back.
- (was line 180) force the spill

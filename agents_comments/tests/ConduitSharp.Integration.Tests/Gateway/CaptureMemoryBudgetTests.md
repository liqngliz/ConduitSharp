# tests/ConduitSharp.Integration.Tests/Gateway/CaptureMemoryBudgetTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## CaptureMemoryBudgetTests.StreamingCapturePlugin.ReadsRequestBody

- (was line 36) Streaming path: the gateway must not buffer the body on this plugin's behalf.

## CaptureMemoryBudgetTests.CaptureMemory_CountsAgainstRamBudget_ShedsWith503

- (was line 92) Capture wants 32 KiB per request against a 4 KiB gateway-wide ceiling: the reservation cannot fit, so the request sheds rather than quietly growing the process.

## CaptureMemoryBudgetTests.CaptureMemory_OnTheBufferedPath_StillCountsAgainstRamBudget_ShedsWith503

- (was line 111) The reservation used to live inside the streaming branch only. Put the same capture plugin on a route that also carries a body-reading plugin and the chain takes the buffered branch, where the declaration was dropped on the floor — capture grew unbudgeted on exactly the routes most likely to have it. Same 32 KiB want against the same 4 KiB ceiling: same 503.

## CaptureMemoryBudgetTests.CaptureMemory_WithinBudget_IsServed_AndReleasedForTheNextRequest

- (was line 132) The other half, and the one a leak would break: the reservation must come back on completion. Serially reusing headroom many times over proves Release runs — without it the ceiling ratchets down and a later request 503s.

## CaptureMemoryBudgetTests.CaptureMemory_IsBoundedByTheRamBudget_NotTheDiskBudget

- (was line 155) Capture is RAM-only — a tee holds pooled segments and never spills — so it meters against the RAM budget and is unaffected by the disk budget. This is the realistic shape of a memory-constrained host with a large spill volume: generous disk, small RAM. A generous disk budget must not license capture to grow in memory.
- (was line 163) 1 MiB — 32 KiB fits easily 4 KiB — 32 KiB does not
- (was line 170) Bounded by the 4 KiB RAM budget; the 1 MiB disk budget is irrelevant to capture.

## CaptureMemoryBudgetTests.CaptureMemory_WithNoRamBudget_Sheds_BecauseCaptureCannotSpill

- (was line 178) MaxRamBufferedBodyBytes = 0 means "no RAM for bodies" — every buffered body spills. A buffered body can honour that; capture cannot, because a captured prefix is held in memory and has no disk path. So capture sheds rather than quietly allocating outside any budget. A generous disk budget does not rescue it: disk is not the resource capture consumes.

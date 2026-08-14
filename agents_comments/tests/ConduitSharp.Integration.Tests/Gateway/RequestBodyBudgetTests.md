# tests/ConduitSharp.Integration.Tests/Gateway/RequestBodyBudgetTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RequestBodyBudgetTests.RamBudget_WhenFull_RefusesRamButDiskStillAdmits

- (was line 26) The step-down itself: no RAM left, so the body spills — but it is still served, because the disk budget has room. A RAM refusal is routine, not an error.
- (was line 34) spilled bytes fit the disk budget → no 503

## RequestBodyBudgetTests.DiskBudget_WhenExhausted_RefusesAndThatIsThe503

- (was line 43) nowhere left to put the body

## RequestBodyBudgetTests.ReleaseRam_OnSpill_ReturnsRamWithoutTouchingTheDiskCharge

- (was line 49) What BufferRequestBody does when a body outgrows its threshold: the rented buffer went back to the pool, so the RAM is genuinely free — and the bytes are now charged to disk.
- (was line 59) RAM freed for the next request the spilled bytes still occupy disk

## RequestBodyBudgetTests.RamBudget_Zero_MeansNoRamAtAll_SoEverythingSpills

- (was line 66) 0 reads the same way on both budgets: none of that resource is available.
- (was line 71) the disk budget still bounds the spill

## RequestBodyBudgetTests.DiskBudget_Zero_MeansNoSpilling_SoABodyMustFitRam

- (was line 80) cannot spill — this is the 503

## RequestBodyBudgetTests.ReleaseDisk_ReturnsBytesToTheDiskBudget

- (was line 93) the finally in BufferRequestBody

## RequestBodyBudgetTests.ConcurrentReserves_NeverOversubscribeEitherBudget

- (was line 99) The budgets are the memory and disk bounds. If the CAS loop is wrong under contention the bound is decorative, so hammer both from every core at once.

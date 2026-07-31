# tests/ConduitSharp.Traffic.Tests/InMemoryCacheServiceTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## InMemoryCacheServiceTests.GetAsync_EmptyCache_ReturnsNull

- (was line 17) Miss

## InMemoryCacheServiceTests.SetThenGet_ReturnsCachedEntry

- (was line 29) Hit

## InMemoryCacheServiceTests.GetAsync_ExpiredEntry_ReturnsNull

- (was line 57) Expired entry

## InMemoryCacheServiceTests.Set_OverwritesExistingKey

- (was line 71) Overwrite

## InMemoryCacheServiceTests.Remove_DeletesEntry

- (was line 86) Remove (cache invalidation)

## InMemoryCacheServiceTests.Remove_MissingKey_IsNoOp

- (was line 101) must not throw

## InMemoryCacheServiceTests.RemoveByPrefix_RemovesMatchingKeysOnly

- (was line 116) untouched

## InMemoryCacheServiceTests.SizeOf

- (was line 120) Total-bytes cap — cache keys are attacker-controlled, so the cache must not grow without bound (mirrors the request-body budget).

## InMemoryCacheServiceTests.Set_OverBudget_EvictsEntryClosestToExpiry

- (was line 131) Budget fits exactly two entries.
- (was line 134) expires first
- (was line 136) over budget
- (was line 138) evicted (closest to expiry)

## InMemoryCacheServiceTests.Set_EntryLargerThanBudget_IsNotCached_AndEvictsNothing

- (was line 152) untouched by the oversized write

## InMemoryCacheServiceTests.Set_ManyDistinctKeys_TotalStaysBounded

- (was line 161) Simulates ?x=1, ?x=2, … key stuffing: far more entries than the budget holds.

## InMemoryCacheServiceTests.RemoveAndOverwrite_ReleaseBudget

- (was line 191) Churn the same two keys: releases must balance reserves or writes start evicting.
- (was line 199) never evicted — budget was never truly exceeded

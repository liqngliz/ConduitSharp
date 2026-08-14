# tests/ConduitSharp.Traffic.Tests/FixedWindowRateLimiterTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## FixedWindowRateLimiterTests.TryAcquire_WithinLimit_ReturnsTrue

- (was line 11) Within limit

## FixedWindowRateLimiterTests.TryAcquire_OverLimit_ReturnsFalse

- (was line 24) Over limit

## FixedWindowRateLimiterTests.TryAcquire_DifferentKeys_HaveIndependentCounters

- (was line 48) Different keys are independent
- (was line 57) independent counter — still allowed

## FixedWindowRateLimiterTests.TryAcquire_ZeroMaxRequests_AlwaysReturnsFalse

- (was line 61) Zero limit

## FixedWindowRateLimiterTests.Denied_RetryAfter_CountsToTheWindowRollover_NotTheFullWindow

- (was line 73) Retry-After — the algorithm owns it, because only it knows when a permit frees
- (was line 79) Windows are epoch-aligned, so 20 s into a 60 s window the answer is 40, not 60. 999_980 % 60 == 20

## FixedWindowRateLimiterTests.Denied_RetryAfter_IsNeverZero_AtTheInstantOfRollover

- (was line 93) 59 s into the window: 1 s remains. A Retry-After of 0 would invite an instant retry. 1_000_019 % 60 == 59

## FixedWindowRateLimiterTests.TryAcquire_ExpiredWindowEntry_IsEvicted

- (was line 112) Eviction — expired window entries are removed to prevent memory leak
- (was line 118) windowId = 1
- (was line 124) windowId = 2
- (was line 127) Old entry (windowId=1) should be evicted; only the current window slot remains.

## FixedWindowRateLimiterTests.TryAcquire_MultipleKeysExpiredWindow_AllEvicted

- (was line 145) All three old entries evicted; only "a" in the new window remains.

## FixedWindowRateLimiterTests.TryAcquire_ConcurrentAcquiresDuringSweep_ExactQuota_ExpiredEvicted_LiveKept

- (was line 152) The sweep is lock-free (CAS elects one sweeper, the rest proceed) and runs while other threads GetOrAdd/Increment the same dictionary. Under contention: the quota must stay exact (no lost or double counts), expired entries must go, and live counters must survive the sweep. windowId = 1
- (was line 163) windowId = 2: every stale entry is expired (ExpiresAt=120) and the sweep is due.
- (was line 174) exactly the quota across 3200 racing attempts 1000 expired gone, the live counter kept and still enforcing

## FixedWindowRateLimiterTests.SharedStore_ShortWindowAcquires_DoNotResetLongWindowCounters

- (was line 180) Shared store, mixed window lengths — regression for cross-scale eviction
- (was line 186) One store serving both a 1 s window and a 60 s window — now the single-singleton path: one limiter, different windows arriving per call. The old eviction compared raw windowIds across entries: a 1 s windowId (~epoch seconds) always exceeded a 60 s windowId (~epoch/60), so every short-window acquire wiped the long window's live counters and its limit was never enforced.
- (was line 197) must not evict the 60 s counter short window rolls over; long window has not
- (was line 201) still over 2-per-60s

## FixedWindowRateLimiterTests.SharedStore_SameKeyDifferentWindows_CountersAreDistinctPerWindowLength

- (was line 207) Same caller key under two window lengths on one shared store — each must track its own count (their windowIds differ in scale).
- (was line 213) own scale — own counter

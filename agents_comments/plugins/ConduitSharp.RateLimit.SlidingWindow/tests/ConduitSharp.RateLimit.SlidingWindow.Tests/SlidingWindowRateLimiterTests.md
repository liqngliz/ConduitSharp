# plugins/ConduitSharp.RateLimit.SlidingWindow/tests/ConduitSharp.RateLimit.SlidingWindow.Tests/SlidingWindowRateLimiterTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## SlidingWindowRateLimiterTests.PermitFreesExactlyWhenTheOldestRequestAgesOut

- (was line 43) The sliding property: the window travels with the request, it does not reset on a boundary. A permit taken at t=0 is returned at t=60s and not one millisecond sooner.
- (was line 51) the original permit is now exactly 60s old

## SlidingWindowRateLimiterTests.RetryAfter_CountsToTheOldestRequestAgingOut_NotToAWindowBoundary

- (was line 58) The reason RateLimitDecision carries a retry hint at all: this answer is knowable only to the algorithm. A fixed window would have said "time until the next aligned boundary", which for a sliding log is meaningless.
- (was line 64) 20s in: the permit frees 40s from now

## SlidingWindowRateLimiterTests.RetryAfter_IsNeverZero_WhenDenied

- (was line 74) A Retry-After of 0 invites an instant retry storm; the contract floors it at 1.
- (was line 78) frees in 1ms — must still round up to a whole second

## SlidingWindowRateLimiterTests.BoundarySpanningBurst_IsRefused_WhereAFixedWindowWouldAllowIt

- (was line 95) The entire reason this algorithm exists, asserted against the one it replaces. Two full quotas either side of an aligned boundary = 2x the nominal rate in one instant. Both limiters see identical calls; only the fixed window lets the burst through.
- (was line 99) 59s into an aligned 60s window
- (was line 109) 61s — past the boundary, so the fixed window's counter resets

## SlidingWindowRateLimiterTests.ExpiredTimestamps_AreDropped_SoMemoryStaysBounded

- (was line 121) Timestamps are swept on use; a key never exceeds maxRequests entries even under sustained load across many windows.

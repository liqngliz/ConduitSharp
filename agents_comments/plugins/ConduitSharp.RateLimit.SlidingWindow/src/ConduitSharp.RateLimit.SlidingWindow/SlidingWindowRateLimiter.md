# plugins/ConduitSharp.RateLimit.SlidingWindow/src/ConduitSharp.RateLimit.SlidingWindow/SlidingWindowRateLimiter.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## SlidingWindowRateLimiter

- (was line 24) Only swept while a key is being used; a key nobody touches holds at most maxRequests timestamps until its next request, when they are dropped as expired.

## SlidingWindowRateLimiter.TryAcquire

- (was line 43) One lock per key, not one global lock: contention is per-caller, and the critical section is a few queue operations bounded by maxRequests.
- (was line 56) A permit frees when the oldest request in the window ages out — the answer a fixed window cannot give, and the reason RateLimitDecision carries the retry hint at all.
- (was line 59) ceil to whole seconds

## SlidingWindowRateLimiter

- Fixed windows are cheap but bursty at the seam: a caller can spend a full quota just before a boundary and another just after, delivering up to 2x the nominal rate across it.
- The trade is memory: O(maxRequests) timestamps per active key against the fixed window's single counter. Worth it for expensive endpoints where the burst is the thing you are paying for, not worth it for a coarse per-minute quota.
- It deliberately does not use IRateLimitStore: that interface counts hits per aligned window id, exactly the model a sliding log rejects. An algorithm is free to keep state a store cannot express, which is why the algorithm and the store are separate seams.

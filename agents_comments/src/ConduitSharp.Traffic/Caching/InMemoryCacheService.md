# src/ConduitSharp.Traffic/Caching/InMemoryCacheService.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## InMemoryCacheService.SetAsync

- (was line 41) larger than the whole budget — don't cache
- (was line 43) replace: release the old entry's bytes first
- (was line 47) ponytail: best-effort cap, not a hard invariant — concurrent SetAsync calls can each pass this check before either evicts, briefly overshooting by up to one entry per writer. Everyone converges on the next write. A hard cap needs a lock around add+evict; take that only if the overshoot ever matters.

## InMemoryCacheService.EvictUntilUnderBudget

- (was line 80) ponytail: evicts by earliest expiry (no LRU access tracking); the O(n log n) snapshot sort only runs when the budget is exceeded.
- (was line 88) KeyValuePair overload: only removes if the entry wasn't concurrently replaced, so the size we subtract always matches the entry we removed.

# src/ConduitSharp.Traffic/RateLimiting/IRateLimitStore.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## InMemoryRateLimitStore

- (was line 25) Sweeping at most this often bounds stale-entry memory without paying a full table scan on every acquire (the hot path with per-caller keys is O(1) now).

## InMemoryRateLimitStore.TryAcquire

- (was line 36) Epoch seconds at the start of the caller's current window — close enough to "now" for sweep scheduling, and derived without the store needing its own clock.
- (was line 44) Each entry records its own absolute expiry: windows of different lengths coexist in this shared store, so comparing raw windowIds across entries (as the old per-acquire eviction did) would let a short-window route evict a long-window route's still-live counters.

## InMemoryRateLimitStore.SweepIfDue

- (was line 78) One thread wins the sweep; the rest skip it and proceed with their acquire.

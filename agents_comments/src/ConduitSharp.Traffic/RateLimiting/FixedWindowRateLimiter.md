# src/ConduitSharp.Traffic/RateLimiting/FixedWindowRateLimiter.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## FixedWindowRateLimiter.TryAcquire

- (was line 45) Seconds until this window rolls over — not the full window length. Boundaries are epoch-aligned, so the answer comes from the clock alone.

## FixedWindowRateLimiter

- The trade: hard window boundaries let a caller spend a full quota at the end of one window and another at the start of the next, up to 2x the nominal rate across that seam. That is the price of O(1) state per key. The SlidingWindow example trades memory for a limit that holds at every instant.

# src/ConduitSharp.Traffic/RateLimiting/FixedWindowRateLimiter.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## FixedWindowRateLimiter.TryAcquire

- (was line 45) Seconds until this window rolls over — not the full window length. Boundaries are epoch-aligned, so the answer comes from the clock alone.

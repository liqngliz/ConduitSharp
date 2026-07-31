# src/ConduitSharp.Traffic/RateLimiting/IRateLimiter.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## IRateLimiter

- This is the algorithm; IRateLimitStore is the counter backend a fixed-window algorithm keeps its counts in. They are separate because the reason to change them differs: a store changes to share counters across replicas (Redis), an algorithm changes what 'over the limit' means. An algorithm need not use a store at all, since a sliding log keeps timestamps, which no store models.

# src/ConduitSharp.Traffic/RateLimiting/RateLimitPlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RateLimitPlugin

- (was line 26) Resolved once: IRateLimiter is a singleton and takes window/quota per call, so one instance serves every route and there is nothing to cache per config.

## RateLimitPlugin..ctor

- (was line 32) DI constructor: picks up a drop-in IRateLimiter (algorithm) and, through it, a drop-in IRateLimitStore (counter backend, e.g. Redis).

## RateLimitPlugin.ExecuteAsync

- (was line 53) The limiter is shared across all routes (and its store may be shared across replicas), so the counter key must carry the route id — otherwise two routes with the same window/max and the same caller would consume each other's quota.
- (was line 59) The algorithm computes its own retry hint: a fixed window rolls over on a boundary, a sliding log frees a permit when its oldest request ages out. Only it knows.

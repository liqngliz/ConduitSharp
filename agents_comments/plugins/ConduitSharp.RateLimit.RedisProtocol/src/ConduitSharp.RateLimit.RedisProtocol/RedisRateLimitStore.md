# plugins/ConduitSharp.RateLimit.RedisProtocol/src/ConduitSharp.RateLimit.RedisProtocol/RedisRateLimitStore.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RedisRateLimitStore

- (was line 34) Weighted add for token metering: INCRBY the cost, set the TTL on the first write of the window.

## RedisRateLimitStore..ctor

- (was line 51) DI constructor: reads its own connection settings straight from configuration — core's GatewayOptions doesn't declare a Redis-shaped property, so this backend's config schema is free to evolve independently of the core package's version.
- (was line 62) start even if Redis is momentarily down
- (was line 70) Test constructor: inject a database directly.

## RedisRateLimitStore.Peek

- (was line 125) Fail-open: a failed peek reads as 0, so the request is allowed through.

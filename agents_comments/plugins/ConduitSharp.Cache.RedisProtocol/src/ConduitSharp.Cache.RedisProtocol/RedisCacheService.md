# plugins/ConduitSharp.Cache.RedisProtocol/src/ConduitSharp.Cache.RedisProtocol/RedisCacheService.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RedisCacheService..ctor

- (was line 30) DI constructor: reads its own connection settings straight from configuration — core's GatewayOptions doesn't declare a Redis-shaped property, so this backend's config schema is free to evolve independently of the core package's version.
- (was line 41) start even if Redis is momentarily down
- (was line 49) Test constructor: inject a database directly.

## RedisCacheService.RemoveByPrefixAsync

- (was line 101) Scan each server (single node in the common case) for matching keys and delete them.

## RedisCacheService.IsRedisFailure

- (was line 119) Redis/transport failures must not surface — the cache degrades to no-op. Serialization bugs (JsonException) are not caught here so they surface in development.

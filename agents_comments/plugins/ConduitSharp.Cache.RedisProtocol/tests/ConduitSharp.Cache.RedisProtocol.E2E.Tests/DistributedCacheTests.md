# plugins/ConduitSharp.Cache.RedisProtocol/tests/ConduitSharp.Cache.RedisProtocol.E2E.Tests/DistributedCacheTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## DistributedCacheTests.CacheSetByOneInstance_IsServedByAnother

- (was line 31) Two independent services (= two gateway instances) sharing one Redis.

## RedisDistributedCacheTests

- (was line 82) The same distributed tests, executed against each backend.

## RedisCacheFailOpenTests.DeadRedis_FailsOpen_NoThrow

- (was line 109) swallowed treated as a miss no-op

# plugins/ConduitSharp.Cache.RedisProtocol/tests/ConduitSharp.Cache.RedisProtocol.Tests/RedisCacheServiceTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RedisCacheServiceTests.Get_RedisFailure_ReturnsNullNotThrow

- (was line 75) Fail-open — Redis errors degrade to no-cache, never surface
- (was line 85) treated as a miss

## RedisCacheServiceTests.Set_RedisFailure_SwallowedNotThrow

- (was line 96) must not throw

# plugins/ConduitSharp.Cache.RedisProtocol/tests/ConduitSharp.Cache.RedisProtocol.E2E.Tests/CacheServerFixture.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RedisCollection

- (was line 8) Two collections so the same distributed tests run against both a Redis and a Valkey server.

## CacheServerFixture.InitializeAsync

- (was line 38) Pull first (separate, generous timeout) so a cold image doesn't fail `docker run`.
- (was line 45) image unavailable / run failed → skip
- (was line 56) clean up the half-started container

## CacheServerFixture.WaitForReadyAsync

- (was line 77) not ready

## CacheServerFixture.RunAsync

- (was line 109) e.g. docker not installed → treated as unavailable

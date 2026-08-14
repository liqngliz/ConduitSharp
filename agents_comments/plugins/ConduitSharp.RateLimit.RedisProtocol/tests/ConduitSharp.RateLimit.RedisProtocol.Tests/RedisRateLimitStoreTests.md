# plugins/ConduitSharp.RateLimit.RedisProtocol/tests/ConduitSharp.RateLimit.RedisProtocol.Tests/RedisRateLimitStoreTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RedisRateLimitStoreTests.Add_ReturnsTheRunningWindowTotal_AndKeysByWindow

- (was line 51) Add and Peek are the token-metering path: token-rate-limit charges through Add and gates on Peek, so a silent break here would let budgets run unenforced across replicas.

## RedisRateLimitStoreTests.NonRedisExceptions_Propagate_RatherThanFailingOpenSilently

- (was line 110) Fail-open covers backend outages. A bug in our own code must still surface.

## RedisRateLimitStoreTests.DisposingATestConstructedStore_DoesNotThrow

- (was line 122) The connection is null on this path; Dispose must tolerate it.

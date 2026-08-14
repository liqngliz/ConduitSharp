# tests/ConduitSharp.Traffic.Tests/CachePluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## CachePluginTests.Plugin

- (was line 13) Helpers

## CachePluginTests.ExecuteAsync_NonCacheableMethod_CallsNextWithoutCaching

- (was line 49) Non-GET / non-HEAD — bypass cache

## CachePluginTests.ExecuteAsync_GetCacheMiss_CallsNext

- (was line 70) GET — cache miss

## CachePluginTests.ExecuteAsync_GetCacheHit_ShortCircuitsWithCachedResponse

- (was line 100) GET — cache hit
- (was line 110) Populate cache manually with the key the plugin would build
- (was line 114) Warm up by injecting the entry

## CachePluginTests.ExecuteAsync_DifferentQueryParams_UseSeparateCacheKeys

- (was line 151) Cache key — vary by headers and query params

## CachePluginTests.ExecuteAsync_VaryByHeader_MatchingHeader_IncludedInKey

- (was line 180) Warm cache for en-US
- (was line 185) Different language — should be a separate cache key → calls next again

## CachePluginTests.ExecuteAsync_GetCacheMiss_CapturesResponse

- (was line 195) Response capture — on a cache miss the plugin swaps Response.Body for a bounded CapturingStream, so the body reaches the client and the cache in one pass
- (was line 207) Captured internally

## CachePluginTests.ExecuteAsync_CaptureCallback_WritesToCache

- (was line 224) The response must now be in the cache.

## CachePluginTests.ExecuteAsync_NonCacheableMethod_DoesNotCache

- (was line 243) Actually we can just check it wasn't cached, or skip.

## CachePluginTests.ExecuteAsync_GetCacheHit_DoesNotCacheAgain

- (was line 261) Short-circuited from cache — no callback needed. Short-circuited from cache — no callback needed.

## CachePluginTests.ExecuteAsync_BinaryBody_RoundTripsByteForByte

- (was line 266) Binary bodies — regression: caching must never decode/re-encode as UTF-8 text
- (was line 277) Bytes that are not valid UTF-8 (e.g. a gzip magic number / arbitrary binary payload). A round-trip through Encoding.UTF8.GetString/GetBytes would corrupt these.

## CachePluginTests.ExecuteAsync_ResponseOverCap_StreamsToClientButDoesNotCache

- (was line 317) Bounded capture — large responses stream to the client but are not cached, and the gateway never buffers past the cacheable-size cap.
- (was line 329) exceeds the 8-byte cap
- (was line 338) Streamed to the client in full despite exceeding the cache cap.
- (was line 342) But not cached — oversized.

## CachePluginTests.ExecuteAsync_NoStoreResponse_StreamsButDoesNotCache

- (was line 360) Delivered to the client...
- (was line 363) ...but honoured no-store: nothing cached.

## CachePluginTests.BuildExpectedKey

- (was line 368) Private key-builder mirror (used to pre-populate the cache in tests)

# tests/ConduitSharp.Integration.Tests/Pipeline/Traffic/CacheEndToEndTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## CacheEndToEndTests.GetCacheHit_SecondRequestServedFromCache

- (was line 36) First request — cache miss, upstream is called and response is stored.
- (was line 41) Second request — cache hit, upstream must NOT be called again.
- (was line 44) still only 1

## CacheEndToEndTests.GetCacheHit_PreservesUpstreamContentType

- (was line 51) Regression: WriteShortCircuitAsync used to clobber the cached Content-Type with text/plain after copying ShortCircuitHeaders.

## CacheEndToEndTests.UpstreamCacheControlOptOut_ResponseIsNotCached

- (was line 70) The cache is explicit-opt-in per route, but an upstream that marks a response no-store (or private — this gateway is a shared cache) must still win.
- (was line 86) not served from cache both requests hit the upstream

## CacheEndToEndTests.GetCacheMiss_UpstreamNonSuccess_ResponseIsForwarded_AndNotCached

- (was line 105) Upstream returns 500 — the cache callback must NOT be invoked, so the second request must still reach the upstream (nothing was cached).
- (was line 118) Both requests must have reached the upstream — the 500 was NOT cached.

## CacheEndToEndTests.LargeResponse_IsCachedWithoutMemoryBlowup

- (was line 123) R4 — response is streamed and captured together (tee), with a size cap
- (was line 129) A 256 KB body (under the 1 MiB default cap) is streamed to the client and captured at the same time — served intact on the miss and identically on the hit.
- (was line 139) streamed intact
- (was line 143) cached intact served from cache

## CacheEndToEndTests.CoalesceRoutes

- (was line 147) Coalescing requires the response-producing plugin (here http-proxy) to run *inside the chain, so the cache plugin's next() encompasses the upstream call.

## CacheEndToEndTests.ConcurrentMisses_AreCoalesced_UpstreamCalledOnce

- (was line 170) Stampede protection: a slow upstream + many concurrent misses for the same key must collapse to a single upstream request; the rest share the leader's result.
- (was line 176) hold the leader long enough for followers to queue
- (was line 188) 9 followers coalesced onto 1 leader

## CacheEndToEndTests.ResponseOverCacheLimit_IsStreamedButNotCached

- (was line 195) A 4 KB body with a 1 KB cache cap: the client still gets the full body (streaming is never interrupted), but capture stops past the cap so nothing is cached — the second request re-fetches from the upstream.
- (was line 206) full body despite exceeding cap
- (was line 210) not a cache hit re-fetched, not cached

## CacheEndToEndTests.Same_plugin_on_four_routes_keeps_separate_configs

- (was line 218) Four distinct cache configs; each route must behave per its OWN config: a: normal cache            -> 2nd GET is a HIT b: maxCacheableBytes = 1   -> nothing cacheable, 2nd GET re-fetches c: varyByHeaders X-Tenant  -> different tenant misses, same tenant hits d: normal cache            -> HIT (b's byte cap must not bleed here)
- (was line 240) a: miss then hit — 1 upstream call.
- (was line 246) b: byte cap of 1 makes the body uncacheable — 2 upstream calls, no hit.
- (was line 252) c: same path, different X-Tenant — separate cache entries (2 upstream calls), then a tenant repeat is a hit.
- (was line 261) d: caches normally — b's maxCacheableBytes=1 did not overwrite this route.

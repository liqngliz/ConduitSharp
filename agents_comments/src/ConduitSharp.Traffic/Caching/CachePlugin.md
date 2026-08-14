# src/ConduitSharp.Traffic/Caching/CachePlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## CachePlugin

- (was line 27) Stampede protection: in-flight upstream fetches keyed by cache key, so N concurrent misses for the same key collapse to one upstream request; the rest share its result. Coalescing only engages when the response-producing plugin (http-proxy or a terminal plugin) runs inside the chain after cache — so the leader's next() encompasses the fetch. If http-proxy is left to the implicit fallback, each request fetches (still correct, just no coalescing).

## CachePlugin.ExecuteAsync

- (was line 53) Become the leader for this key, or join an in-flight fetch as a follower.
- (was line 59) Someone else is already fetching this key — wait and share their result.
- (was line 66) The leader produced nothing cacheable (error / non-2xx / oversized) — fetch ourselves.
- (was line 71) Leader: fetch the upstream while teeing the response into a bounded capture buffer, publish the result to followers, then release. The tee writes through to the real body so the client streams in real time; capture stops once MaxCacheableBytes is exceeded, so a large uncacheable response is never buffered in full.
- (was line 107) release any waiters the leader did not already satisfy

## CachePlugin.IsCacheable

- (was line 111) A response is cacheable unless it opts out via Cache-Control: no-store or private — some routes legitimately mark per-user responses uncacheable even on a cached path.

## CachePlugin.CapturingStream

- (was line 121) Write-through stream that tees the response body into a buffer up to a byte cap. Past the cap it stops capturing (marks Overflowed, drops the buffer) but keeps streaming to the client — bounding gateway memory to the cacheable-size limit. ponytail: swapping Response.Body reroutes Response.Body/WriteAsync/BodyWriter through this stream (StreamResponseBodyFeature); a plugin that grabs a raw IHttpResponseBodyFeature and bypasses it would escape capture — no built-in plugin does. Wrap the feature if one ever does.

## CachePlugin.ServeAsync

- (was line 187) The exact body length is known, so say so — without it HTTP/1.1 falls back to chunked transfer, which the original upstream response did not use.

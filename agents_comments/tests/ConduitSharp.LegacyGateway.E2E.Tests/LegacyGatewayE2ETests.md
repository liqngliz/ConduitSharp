# tests/ConduitSharp.LegacyGateway.E2E.Tests/LegacyGatewayE2ETests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## LegacyGatewayE2ETests.PostUpload_WithStreamOnly_Succeeds_AndStreamingCaptureLogsPrefixWithRouteId

- (was line 22) Uploads — streamOnly; body-capture-streaming tees a bounded prefix off the streaming path (the buffered body-capture variant is startup-rejected here)
- (was line 52) The streaming tee logged the body prefix, attributed to the matched route.
- (was line 56) The buffered variant must NOT have run — this route has no rewindable body to give it.

## LegacyGatewayE2ETests.Upstream_node_crash_is_absorbed_by_circuit_breaker

- (was line 61) Resilience — a node crash mid-traffic is absorbed, not amplified
- (was line 67) Hard-kill inventory node-1 (port 5102 — chosen because the /health route and swagger fetchFrom pin node-0, so nothing else in the suite depends on this node). Contract: clients may see at most a handful of 502s while the breaker counts failures (threshold 2, routes.json), never a 500 or a hang; once the circuit opens, round-robin converges on the survivor and stays clean for the cooldown.

## LegacyGatewayE2ETests.SlidingWindowLimiter_IsDiscovered_AndEnforcesPerClientQuota

- (was line 92) Rate limiting — drop-in SlidingWindowRateLimiter (IRateLimiter) wired via the plugins root. The /api/ratelimit-demo route carries a tiny per-client quota (maxRequests: 3, windowSeconds: 30), so a 4th request inside the window trips 429.
- (was line 100) The host logs the drop-in algorithm it registered at startup — proof the *sliding limiter is active, not the built-in fixed window (both would 429, only this line distinguishes them).
- (was line 107) A unique client key isolates this burst from any other caller/run against the shared per-route counter.
- (was line 118) First 3 pass the limiter (forwarded upstream — any non-429 status); the 4th is denied.
- (was line 122) The algorithm supplies its own Retry-After — a sliding log answers "seconds until your oldest request ages out", and it must be a positive whole number of seconds.

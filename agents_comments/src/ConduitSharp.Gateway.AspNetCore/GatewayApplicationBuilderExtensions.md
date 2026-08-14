# src/ConduitSharp.Gateway.AspNetCore/GatewayApplicationBuilderExtensions.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GatewayApplicationBuilderExtensions.UseConduitSharpGateway

- (was line 42) Resolved eagerly so its configuration is validated at startup rather than on the first request that happens to buffer — a removed or negative limit should fail the host, not one unlucky request much later.
- (was line 56) Aggregated Swagger UI is an optional add-on (ConduitSharp.Gateway.AspNetCore.Swagger): call app.UseConduitSharpGatewaySwagger() before this method to enable it.
- (was line 59) One gateway.request span per request the gateway handles — including the ones that match no route, so a 404 is still traceable. Sits after admin/health, which answer and return before reaching it. The route id is tagged on later, from inside the matched route's chain.
- (was line 63) The same finally notifies every IRequestObserver (structured request log, OTel request counter/duration/error metrics) — this is the only place per-request observability fan-out happens, so it must sit on the outermost path where timing covers everything.
- (was line 99) An observer must never be able to fail the request from inside a finally.
- (was line 103) observers are fire-and-forget by contract
- (was line 111) The route table owns everything a hot reload must swap together: one compiled RequestDelegate per route (plugins resolved once, not per request), the route lookup, plugin-only endpoints, retry pipelines, and YARP's in-memory config.
- (was line 120) Plugins run inside YARP's per-proxy pipeline, so the forwarder always executes within their next() — a cache plugin's response tee therefore wraps the real forward, and a plugin that skips next() short-circuits before YARP forwards anything.
- (was line 135) Custom MatcherPolicies dropped into the plugins root read the route off the endpoint.
- (was line 138) Plugin-only routes never reach YARP: it rejects a route with no cluster before any middleware runs, so the table serves them as ordinary endpoints running the same chain — through a mutable data source, so a hot reload can add and remove them like YARP routes.
- (was line 143) No catch-all fallback endpoint: one would match every path and so mask endpoint routing's own 405, turning "right path, wrong verb" into a 404. Unmatched requests get the framework's 404 (and, under a PathPrefix, fall through to the host).

## GatewayApplicationBuilderExtensions.ValidateRouteTable

- (was line 150) Route-table validation. Runs at startup and again against the incoming table on admin reload, so a reload can never swap in routes the gateway cannot serve.

## GatewayApplicationBuilderExtensions.ValidateLoadBalancingPolicies

- (was line 159) Every route's loadBalancingStrategy must name a registered ILoadBalancingPolicy — YARP's five built-ins plus anything dropped into the plugins root. Checking against the DI set rather than a hardcoded list means drop-in policies validate for free, and the error can name what is actually available. YARP would catch this too, but later and less usefully.

## GatewayApplicationBuilderExtensions.ValidatePluginChains

- (was line 182) Every enabled plugin must resolve and its config must parse, and 'http-proxy' (when named explicitly) must sit last, because it is where the forward happens.
- (was line 201) 'http-proxy' is no longer a plugin — it names the forward step itself.
- (was line 214) Key by id + variant so each custom variant logs its own winner; the source tag makes a silent last-registration-wins override visible at startup.

## GatewayApplicationBuilderExtensions.ResolvePlugin

- (was line 253) Last registration wins, so a drop-in DLL shadows the built-in of the same name.

## GatewayApplicationBuilderExtensions.BuildRouteChain

- (was line 260) Per-route chain: telemetry + error boundary → body budget → plugins → forward. Compiled once at startup into a single RequestDelegate.
- (was line 291) Resolved once when the chain is compiled, not per request: plugins are singletons and the route's plugin set is fixed until the next reload rebuilds the chain. Validation already proved they resolve, so a miss here is a bug worth throwing on. Resolving up front also lets the buffering decision below see ReadsRequestBody.
- (was line 304) The buffer exists for exactly two consumers: the retry rewind and body-reading plugins. A route with neither streams by definition — same path as streamOnly, no config needed. (Explicit streamOnly still forces it, and is still validated against body-reading plugins / retry at startup.)
- (was line 311) Plugins that capture hold memory the request-body buffer never accounts for. Bounded per request, but it multiplies by concurrency with nothing to shed it — so reserve it against the same budget the buffered path uses, and 503 at the same ceiling. Summed once here because plugins are singletons and route config is fixed until the next reload rebuilds this chain.
- (was line 318) Reserved ahead of the buffering decision rather than inside one branch of it. Capture memory is held on either path, and while only a streaming-path plugin could declare it when this was written, a capture plugin sharing a route with a body-reading plugin or a retry policy takes the buffered branch — where the declaration used to be dropped on the floor. That is precisely the shape the reservation exists to prevent: a bounded per-request cap multiplied by unbounded concurrency, with nothing shedding load. Reserving first also sheds before the body is buffered, not after.
- (was line 329) Also applied here so a shed request is still held to the route's body limit — the branch below never runs on this path. Setting a feature twice is harmless.
- (was line 334) Capture is RAM-only — a tee holds pooled segments, a prefix read rents from ArrayPool, and neither ever spills — so it charges the RAM budget. Unlike a body, there is no disk to fall back to, which makes a refusal here the 503.
- (was line 350) Released on every path — an abort or an upstream throw must not leak the reservation, or the ceiling ratchets down until the gateway 503s permanently.

## GatewayApplicationBuilderExtensions.BufferRequestBody

- (was line 416) Buffer the request body so the retry loop can rewind it and body-reading plugins get a seekable stream, enforcing the per-route size limit (413) and the gateway-wide buffering budget (503).
- (was line 420) Buffering degrades in two tiers rather than one cliff. While the memory tier (MaxRamBufferedBodyBytes) has headroom a body buffers in RAM, which measures ~3-5x faster than spilling. Once it is full, further bodies spill to a temp file from the first byte — slower, but still served — until the disk budget (MaxDiskBufferedBodyBytes) is gone and the gateway sheds with a 503.
- (was line 426) The per-request RAM ceiling can be generous (up to 1 MiB) precisely because the memory tier caps the aggregate. Above 1 MiB, FileBufferingReadStream stops renting from ArrayPool and grows a bare MemoryStream by doubling — ~2x the body allocated on the LOH — which is why the threshold is clamped there and not left to the operator.
- (was line 431) Non-idempotent methods on retry-only routes stream: the retry loop can never replay them, so their buffer would have no consumer — unless the route opts in with retryNonIdempotent, which makes the loop replay them and so requires the buffer.
- (was line 464) A body larger than the bigger of the two budgets can land nowhere — shed before reading it rather than streaming it in only to refuse it chunk by chunk.
- (was line 474) Ask the RAM budget for this body's ceiling. The reservation covers the buffer's capacity, not its fill: FileBufferingReadStream rents the whole threshold from ArrayPool at construction and hands it back the instant it spills. A refusal is routine, not an error — threshold 0 means "spill from the first byte", which is the disk budget doing its job, and the request is still served.
- (was line 480) Only a 4 KiB floor is enforced. Up to 1 MiB the threshold is served from ArrayPool; above it FileBufferingReadStream grows a bare MemoryStream that doubles on the large object heap (~2x the body). That is a real cost, but it is the operator's to accept — bodies that fit in RAM at a raised threshold skip the spill entirely — so the ceiling is configurable, not clamped. The RAM budget still bounds the aggregate.
- (was line 488) A body Content-Length already proves cannot fit in RAM gains nothing from a memory buffer: it would rent the threshold, fill it, then copy every one of those bytes to disk anyway. Spilling from the first byte skips that copy and leaves RAM for bodies that can actually be served out of it. Chunked bodies have no Content-Length, so they still try — being wrong there just costs the copy this branch avoids.
- (was line 503) known too big, no headroom, or no RAM budget — spill this one
- (was line 529) While the body is still in RAM its bytes are already covered by the capacity reservation taken above, so there is nothing to charge per chunk. Only spilled bytes are metered here, against the disk budget.
- (was line 534) Just crossed over: FileBufferingReadStream has copied everything read so far to the temp file and returned its rented buffer to the pool. Charge the whole body so far to disk, then hand the RAM back so the next request can use it.
- (was line 571) no-op if the spill already handed it back deletes the spill file, if any

## GatewayApplicationBuilderExtensions.MapAdminApi

- (was line 596) Admin API — POST /admin/routes/reload, DELETE /admin/cache/{routeId} Registered as Use() middleware BEFORE the gateway's endpoints. Disabled entirely when Gateway:AdminKeyHash is null or empty. The incoming X-Admin-Key header value is SHA-256 hashed before comparison — the raw secret is never stored in config.
- (was line 607) Decoded once at startup: a malformed Gateway:AdminKeyHash should fail the gateway, not every admin request. FixedTimeEquals also needs the raw bytes, not the hex text.
- (was line 633) FixedTimeEquals, not string.Equals: this is the only endpoint that takes a secret, and an ordinary comparison returns as soon as two bytes differ. That timing signal is enough to recover the expected hash byte by byte over many requests.
- (was line 648) DELETE /admin/cache/{routeId} — flush a route's cached responses.
- (was line 664) POST /admin/routes/reload — everything below handles the reload.
- (was line 682) Same gate as startup: reject a table naming an unregistered load-balancing policy or plugin, or a plugin config that does not parse — nothing is swapped on failure.
- (was line 697) Atomic swap (O4): write to a temp file in the same directory, then rename over the target. routes.json is therefore never left partially written — a crash mid-write leaves the old, valid file intact rather than corrupt config.
- (was line 712) Audit trail (O5): structured log + counter + span event, so who reloaded what and when is observable.
- (was line 728) Hot swap: compile chains and rebuild YARP's route/cluster config in place — no host restart, in-flight requests unaffected. The shared GatewayRoutesConfiguration list is refreshed so /readyz and other readers see the new table.

## GatewayApplicationBuilderExtensions.MapHealthEndpoints

- (was line 741) Gateway-owned health endpoints (answered by the gateway itself, never proxied): /healthz — liveness: the process is up. /readyz  — readiness: a route table is loaded and the gateway can serve. Deliberately independent of upstream reachability — a downstream blip must not pull every gateway replica out of rotation (correlated-failure anti-pattern).

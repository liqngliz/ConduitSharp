# tests/ConduitSharp.Integration.Tests/Gateway/SecurityHardeningTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## SecurityHardeningTests.RequestBody_NormalSize_IsForwardedCorrectly

- (was line 20) S1 — Request body size limit
- (was line 30) 1 KB

## SecurityHardeningTests.RetryRoutes

- (was line 39) Routes buffer only when something consumes the buffer (retry rewind or a body-reading plugin) AND the method is retryable — everything else streams. Buffered-path tests use a retry route + PUT (idempotent → buffers); streaming-path tests use plain routes/POST.

## SecurityHardeningTests.RequestBody_ExceedsLimit_Returns413

- (was line 53) 10 MB > 8 MiB default limit

## SecurityHardeningTests.RequestBody_DiskBufferBudgetExceeded_Returns503

- (was line 98) Per-request limit admits the body, but it is too large for the RAM threshold, so it must spill — and the shared disk budget has no room for it.

## SecurityHardeningTests.RequestBody_NoBufferConsumer_StreamsAndIgnoresBudget

- (was line 119) No retry, no body-reading plugin → nothing consumes a buffer → the route streams automatically. The buffering budget must not apply.

## SecurityHardeningTests.RequestBody_NonIdempotentMethodOnRetryRoute_StreamsAndIgnoresBudget

- (was line 139) Retry route, but POST never retries — its buffer would have no consumer, so it streams and the budget must not apply.

## SecurityHardeningTests.RequestBody_LargerThanMemoryThreshold_SpillsToDiskAndForwardsIntact

- (was line 159) Buffered path with a body well past RamBufferThresholdBytes: FileBufferingReadStream spills to a temp file; the forwarded bytes must be identical. The threshold is pinned rather than left to the default — otherwise raising the default (it is now 1 MiB) silently parks this body in memory and the spill goes untested.
- (was line 170) ASCII pattern — FakeUpstream captures the body as text, so keep it encoding-safe.

## SecurityHardeningTests.RequestBody_StreamOnly_BypassesTotalBufferBudget_Returns200

- (was line 186) streamOnly avoids BufferRequestBody, so the buffering budget is not consumed.

## SecurityHardeningTests.BodyReadingPlugin

- (was line 205) A plugin that reads the body cannot run on a streamOnly route (no buffered/seekable body). The gateway must reject that pairing at startup, not hand the plugin a forward-only stream.

## SecurityHardeningTests.BodyReadingPlugin.ExecuteAsync

- (was line 217) Same contract BodyCapturePlugin relies on: seekable, rewindable, pre-buffered.

## SecurityHardeningTests.BodyReadingPlugin_OnPostRoute_ForcesBufferAndForwardsIntact

- (was line 229) ReadsRequestBody forces the buffered path even for POST (which would otherwise stream): the plugin must see the full body AND the upstream must still get it. Body > RamBufferThresholdBytes so the read spans the disk-spill boundary.
- (was line 248) plugin saw the whole body
- (was line 250) upstream still got it all

## SecurityHardeningTests.RequestBody_ExceedsRouteLimit_Returns413_EvenWhenGlobalAllows

- (was line 306) 2 KB is under the 8 MiB global default but over the route's 1 KB limit. PUT: buffered-path enforcement (route has retry; POST would stream).

## SecurityHardeningTests.RequestBody_RouteLimitRaisesGlobal_LargeBodyIsForwarded

- (was line 327) 4 KB exceeds the 1 KB global default but the route allows up to 1 MiB.

## SecurityHardeningTests.RequestBody_UnmatchedRoute_Returns404WithoutBuffering

- (was line 357) Route matching runs before body buffering — an oversized body to an unmatched path gets 404, not 413, and is never read into memory.

## SecurityHardeningTests.RequestBody_BudgetIsReleased_SequentialRequestsSucceed

- (was line 375) Budget admits one 4 KB body at a time; sequential requests must all pass, proving reservations are released when a request completes.

## SecurityHardeningTests.RequestBody_LargeBody_DoesNotCrashGateway

- (was line 397) Documents current behaviour: large bodies don't crash the gateway — they are forwarded. This test should remain passing once S1 is implemented (it will return 413 instead of 200, which is fine — the gateway doesn't crash either way).
- (was line 405) 5 MB

## SecurityHardeningTests.SwaggerFetchFromRoutes

- (was line 415) S2 — SSRF in Swagger spec fetching (fetchFrom)

## SecurityHardeningTests.SwaggerFetch_ConnectionRefused_Returns502NotCrash

- (was line 434) Port 1 on loopback is not listening — guaranteed connection refused.
- (was line 442) Gateway must not crash or return 5xx with an unhandled exception. It should return 502 Bad Gateway with an error message.

## SecurityHardeningTests.SwaggerFetch_PrivateIpRange_IsBlocked

- (was line 450) AWS metadata IP — should be blocked before making any network call.
- (was line 458) After fix: expect 400 or 403, not a real network attempt that might succeed.

## SecurityHardeningTests.SwaggerFetch_AllowlistedHost_IsAttempted

- (was line 468) A non-loopback, non-upstream host is normally refused with 403 — but when listed in Gateway:Swagger:AllowedSpecHosts the fetch is attempted, surfacing as 502 here because the name does not resolve.

## SecurityHardeningTests.SwaggerFetch_ErrorMessage_DoesNotLeakInternalUrlDetails

- (was line 488) The 502 body must stay generic: exception messages carry the target URL.

## SecurityHardeningTests.SwaggerSpecFileRoutes

- (was line 504) S3 — Path traversal in Swagger specFile

## SecurityHardeningTests.SwaggerSpec_WithAuthPlugins_InjectsSecuritySchemes

- (was line 552) The aggregated spec injects OpenAPI security schemes derived from the route's plugin list — apiKey for api-key-auth, http bearer for jwt-auth.

## SecurityHardeningTests.SwaggerSpec_BearerDescription_DefaultsToGeneric_AndIsConfigurable

- (was line 599) The bearer scheme's description must not hardcode example-specific instructions (e.g. a demo-token script) in the core library — it's a deployment-level setting.
- (was line 623) Default — no custom description configured.
- (was line 632) Deployment-configured description flows through to the served spec.

## SecurityHardeningTests.SwaggerSpec_PathTraversal_IsBlocked

- (was line 661) Traversal: resolves to /etc/hosts (exists on macOS/Linux)
- (was line 670) After fix: must not serve /etc/hosts contents.

## SecurityHardeningTests.RouteId_WithPathSeparator_IsRejectedAtStartup

- (was line 685) S4 — Route ID used as filesystem directory name
- (was line 702) Startup must fail: WebApplicationFactory builds the host lazily, so the startup validation fires on first CreateClient(), not on CreateAsync.

## SecurityHardeningTests.AdminApi_WithoutKeyConfigured_IsNotExposed

- (was line 716) S5 — Admin API hardening
- (was line 746) Admin path has no route match → GatewayMiddleware returns 404.

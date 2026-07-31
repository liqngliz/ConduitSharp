# tests/ConduitSharp.Integration.Tests/Gateway/AdminApiTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## AdminApiTests

- (was line 22) SHA-256 of "test-key"
- (was line 26) SHA-256 of "correct-key"

## AdminApiTests.Reload_NoAdminKeyConfigured_TreatedAsNormalRequest_Returns404

- (was line 40) No admin key configured — endpoint not registered; /admin path has no route match, so GatewayMiddleware returns 404.
- (was line 51) Routes only match /api/** — admin path gets no match → 404

## AdminApiTests.Reload_WrongAdminKey_Returns401

- (was line 78) Wrong admin key → 401

## AdminApiTests.Reload_InvalidJson_Returns400

- (was line 100) Correct key but invalid JSON → 400, file unchanged

## AdminApiTests.Reload_ValidJson_Returns200AndWritesFile

- (was line 152) Correct key + valid routes JSON → 200, file written, routes swapped in place

## AdminApiTests.Reload_NewRouteTable_ServesNextRequest_WithoutRestart

- (was line 199) Hot reload — the new route table serves the very next request, no restart
- (was line 209) Start with a route that only matches /old.
- (was line 230) Swap in a table where /new forwards and /old is gone.
- (was line 253) Same process, same client — the endpoint table swapped underneath.

## AdminApiTests.Reload_PluginOnlyRoute_IsHotSwappedToo

- (was line 267) A plugin-only route ("cluster": null) is served outside YARP, so it has its own endpoint data source — it must reload alongside the proxied routes.
- (was line 287) No upstream, no plugin produced a response → the chain's terminal 502.

## AdminApiTests.Reload_UnknownLoadBalancingStrategy_Returns400_AndKeepsServingOldRoutes

- (was line 303) The reload runs the same gate as startup, so a policy name nothing registers is caught here rather than blowing up YARP's config load after the table has been swapped.
- (was line 329) The original table is untouched and still serving.

## AdminApiTests.Reload_UnregisteredPlugin_Returns400_AndKeepsServingOldRoutes

- (was line 344) "custom" with a variant nothing registers — must be rejected before anything swaps.
- (was line 368) The original table is untouched and still serving.

## AdminApiTests.Reload_AtomicWrite_LeavesNoTempFiles

- (was line 374) O4 — atomic write leaves no temp files behind
- (was line 407) The temp file used for the atomic swap must have been renamed away, not left behind.

## AdminApiTests.Reload_EmitsAuditReloadCounter

- (was line 413) O5 — a successful reload increments the audit counter

## AdminApiTests.InvalidateCache_ByRoute_RemovesCachedEntry_NextRequestReFetches

- (was line 456) DELETE /admin/cache/{routeId} — cache invalidation
- (was line 482) Prime the cache: first GET is a miss (upstream hit), second is a HIT (no upstream hit).
- (was line 487) Invalidate the route's cache.
- (was line 494) Next GET must miss and re-fetch from the upstream.

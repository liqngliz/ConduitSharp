# tests/Shared/E2E/GatewayE2ETestsBase.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GatewayE2ETestsBase.GetHealth_Returns200

- (was line 47) Health

## GatewayE2ETestsBase.GetInventory_WithApiKey_Returns200

- (was line 59) Inventory — API-key auth, rate-limited, round-robin load balanced

## GatewayE2ETestsBase.DeleteInventory_MethodNotInRoute_Returns405

- (was line 107) The inventory route only allows GET and POST — DELETE has no match. ASP.NET Core native routing correctly returns 405 Method Not Allowed.

## GatewayE2ETestsBase.GetOrders_WithJwt_Returns200

- (was line 117) Orders — JWT (HS256) auth

## GatewayE2ETestsBase.PostOrders_WithJwt_BodyCaptureLogsBody

- (was line 142) BodyCapture plugin should have written to gateway.log
- (was line 151) These routes take the plugin's HttpLogging tee branch, which emits a multi-line structured block ("RequestBody: ..."), not the single "Captured request body for path ..." line the buffer-reuse branch writes. The body literal is unique to this request, so matching it alone proves both that capture ran and what it captured.

## GatewayE2ETestsBase.GetOrders_WrongSignatureToken_Returns401

- (was line 189) Valid structure, wrong signing key.

## GatewayE2ETestsBase.GetErpValue_WithJwt_Returns200_OrSkippedIfNoPwsh

- (was line 201) Erp value report — JWT auth + role-based access, rate limited, cached 60s, executed via PowerShell plugin
- (was line 210) PowerShell plugin requires pwsh — skip gracefully on machines without it.

## GatewayE2ETestsBase.GetErpValue_ValidTokenWrongRole_Returns403_OrSkippedIfNoPwsh

- (was line 236) Same signing key/issuer/audience as fx.DemoJwt, but a role not in the route's requiredClaims anyOf list — a valid token lacking permission is 403, not 401.

## GatewayE2ETestsBase.GetErpValue_CalledTwice_SecondResponseIsCached

- (was line 254) skip if pwsh fails for other reasons
- (was line 262) Cache TTL is 60s — both responses must be identical.

## GatewayE2ETestsBase.UnmatchedPath_Returns404

- (was line 268) Unmatched routes

## GatewayE2ETestsBase.SwaggerSpec_InventoryRoute_Returns200OrBadGateway

- (was line 280) Swagger spec aggregation
- (was line 286) Returns 200 when the upstream is live; 502 when upstream is unreachable. Either is acceptable here — we just verify the gateway endpoint itself is wired.

## GatewayE2ETestsBase.PostInventory_BodyOverRouteLimit_Returns413

- (was line 316) Security hardening — body limits (S1), auth boundaries, error hygiene (S5) Exercised against the real gateway process with the shipped routes.json.
- (was line 323) The inventory route ships with maxRequestBodyBytes = 1 MiB (routes.json), overriding the 8 MiB global default — 2 MiB must be rejected at the gateway.
- (was line 328) Expect: 100-continue — the gateway 413s on Content-Length and aborts without draining; without this the body write races the abort into a broken pipe.

## GatewayE2ETestsBase.PostOrders_BodyOverGlobalLimit_Returns413

- (was line 340) The orders route has no per-route override — the 8 MiB global default applies.

## GatewayE2ETestsBase.PostInventory_OversizedBody_ErrorBodyIsGeneric

- (was line 354) Rejection bodies must not echo limits, paths, or internal detail.
- (was line 357) Without a content type the upstream answers the 100-continue with 415, so the body is never sent and the size limit never fires. Same header the orders sibling sets.

## GatewayE2ETestsBase.SwaggerSpec_ServedSpec_DoesNotLeakUpstreamTopology

- (was line 374) The aggregated spec rewrites servers so "Try it out" targets the gateway — upstream node URLs must not appear in the document.
- (was line 377) 502 covered elsewhere

## GatewayE2ETestsBase.PostInventory_SlowUpload_AbortsDueToDataRateLimit

- (was line 388) The gateway is configured with MinRequestBodyDataRate = 500 bytes/sec and 3s GracePeriod. Trickling 100 bytes every 0.5s (~200 bytes/sec) for >3s will trigger Kestrel's slowloris defense and abort the connection.
- (was line 394) Kestrel kills the connection mid-upload, manifesting as an IOException wrapping a socket error or an HttpRequestException.

## GatewayE2ETestsBase.SlowHttpContent.SerializeToStreamAsync

- (was line 408) 200 bytes/sec

## GatewayE2ETestsBase.GrpcSayHello_ThroughGateway_RepliesOverHttp2

- (was line 420) gRPC passthrough — YARP forwarder, HTTP/2 end-to-end
- (was line 426) Cleartext gRPC: the client connects with HTTP/2 prior knowledge to the gateway's Http2-only listener, the greeter-grpc route is forwarded by YARP (h2c upstream), and the server reports the protocol it observed — asserting HTTP/2 survived both hops. No plugin needed: protocol fidelity is what the forwarder is for. The generated client owns the request path, so a prefixed stack rewrites it via a handler rather than the address.

## GatewayE2ETestsBase.GrpcPath_OverHttp1_IsNotServedByGrpcRoute

- (was line 464) A plain HTTP/1.1 POST to the gRPC path must not crash the gateway; the upstream is HTTP/2-only, so anything but a 200-with-grpc-response is fine.

## GatewayE2ETestsBase.Traces_AfterGatewayTraffic_GatewayRequestSpanIsWrittenToTraceFile

- (was line 475) OpenTelemetry — file exporter (the native stack's trace pipeline)
- (was line 481) The native launcher enables the file exporter (configuration-vm/appsettings.json), writing spans as JSON lines. Drive a request, then poll the file for the gateway.request span — this covers the whole OTel pipeline: span creation, processor, exporter. Regressions in any of them surface here.

## GatewayE2ETestsBase.Traces_GatewayRequestSpan_CarriesAlignedInstrumentationScope

- (was line 509) The gateway.request span must name its instrumentation scope (ConduitSharp.Gateway) and report a scope version that tracks the package version — auto-aligned from AssemblyInformationalVersion, SourceLink's "+<commit>" suffix stripped — not a stale hardcode. Proven end to end against the real running gateway, not just the source.
- (was line 529) SourceLink suffix stripped aligned, not the old hardcode starts with a SemVer core

## GatewayE2ETestsBase.FindSpan

- (was line 541) Parses JSON-lines trace output, returning the first span whose "name" matches, or null.
- (was line 547) skip a half-written trailing line mid-append

## GatewayE2ETestsBase.ReadSharedAsync

- (was line 557) Helpers

## GatewayE2ETestsBase

- (was line 589) Same signing key as the fixtures' MintDemoJwt — a real per-file constant would be one more shared indirection for a value that generate-token.sh/.ps1 already duplicate.

# tests/ConduitSharp.LegacyGateway.E2E.Tests/HostileConfigE2ETests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## HostileConfigE2ETests.S2_FetchFromMetadataEndpoint_Returns403WithoutLeak

- (was line 20) S2 — SSRF via swagger fetchFrom

## HostileConfigE2ETests.S3_SpecFileTraversal_Returns400WithoutFileContents

- (was line 46) S3 — specFile path traversal
- (was line 68) /etc/hosts contents resolved path echo

## HostileConfigE2ETests.S5_FetchFromUnreachableUpstream_502BodyIsGeneric

- (was line 73) S5 — error bodies must not leak internal topology
- (was line 79) Loopback is allowlisted by default, so the fetch is attempted and fails — the 502 body must not reveal where the gateway tried to go.

## HostileConfigE2ETests.S6_TraversalRouteId_GatewayRefusesToStart

- (was line 102) S6 — hostile route ID must prevent startup, before any directory I/O
- (was line 124) The traversal target must never have been created: validation runs before SyncPluginDirectories touches the filesystem.

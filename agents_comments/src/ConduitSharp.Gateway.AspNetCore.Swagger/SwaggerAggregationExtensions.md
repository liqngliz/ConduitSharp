# src/ConduitSharp.Gateway.AspNetCore.Swagger/SwaggerAggregationExtensions.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## SwaggerAggregationExtensions.UseSwaggerAggregation

- (was line 51) Serve each route's OpenAPI spec at {swaggerPath}/{routeId}.json
- (was line 61) Extract routeId from {swaggerPath}/{routeId}.json
- (was line 92) Keep the body generic: exception messages carry internal detail (upstream URLs, file paths) that must not reach the client (S5).
- (was line 104) Swagger UI — dropdown contains one entry per swagger-enabled route

## SwaggerAggregationExtensions.ResolveSpecAsync

- (was line 127) SSRF guard (S2): refuse before any network I/O unless the target host is loopback, one of this route's own upstream nodes, or explicitly listed in Gateway:Swagger:AllowedSpecHosts. Blocks cloud metadata endpoints (169.254.169.254), internal services, etc. from operator typos or attacker-supplied route config.
- (was line 141) A route may always fetch its own upstream's spec — same host it already forwards to.
- (was line 162) Containment check: the resolved path must stay under Gateway:BasePath. The trailing separator prevents prefix attacks ("/base-evil" passing a check against "/base"). Blocks "../../etc/hosts"-style traversal (S3).
- Applies two gateway-layer transforms to every spec before serving: rewrite servers so "Try it out" calls the gateway rather than the upstream, and inject security schemes derived from the route's plugin pipeline.
- Uses Microsoft.OpenApi's typed model rather than hand-built JsonObject nodes. Verified before switching: a typed round-trip preserves vendor extensions (x-generated-by, x-team, x-rate-limit all survived), so the old worry that a library would silently drop what it cannot model was unfounded. The cost is two packages on a project that previously had one dependency with none of its own.
- Serialised back in whichever version the reader detected, so a 2.0 spec is not silently upgraded to 3.0.

## SwaggerAggregationExtensions.InjectSecurityFromPlugins

- (was line 193) Security injection
- (was line 249) Merge into existing components object (preserve other entries).

## SwaggerAggregationExtensions.RewriteServersOnly

- Exists because the typed reader throws on documents it cannot model, and the first attempt at this rewrite served those verbatim. That leaked the upstream's own host and port into the served spec and broke SwaggerSpec_ServedSpec_DoesNotLeakUpstreamTopology in all three E2E suites.
- The raw-JSON approach got that guarantee for free: it patched servers without needing to understand the document. The typed path has to fall back explicitly to keep it.
- Security schemes are skipped on this path. That only costs the "Try it out" affordance, whereas publishing internal topology is a real leak, so the two are not traded evenly.
- Do not relax this to `return json`. SwaggerSpec_UnparseableUpstreamSpec_IsServedVerbatim pins both halves: unmodellable content passes through, servers does not.

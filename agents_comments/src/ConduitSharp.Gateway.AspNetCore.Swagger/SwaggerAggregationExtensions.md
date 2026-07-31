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
- (was line 178) Parse and apply gateway-layer transforms to every spec before serving: 1. Rewrite servers → empty string so "Try it out" calls the gateway, not the upstream. 2. Inject security schemes derived from the route's plugin pipeline.

## SwaggerAggregationExtensions.InjectSecurityFromPlugins

- (was line 193) Security injection
- (was line 249) Merge into existing components object (preserve other entries).

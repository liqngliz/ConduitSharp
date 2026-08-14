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

## SwaggerAggregationExtensions.InjectSecurityFromPlugins

- (was line 193) Security injection
- (was line 249) Merge into existing components object (preserve other entries).

## SwaggerAggregationExtensions.InjectSecurityFromPlugins

- Patches raw JSON rather than using an OpenAPI object model, and that is deliberate after trying the alternative. Microsoft.OpenApi 1.6.x cannot read OpenAPI 3.1, ASP.NET Core 10 emits 3.1.1 by default, and every upstream in the examples serves 3.1.1. Swapping to the typed model silently stopped injecting security schemes on every real route while still passing the in-process tests, because those use a hand-written 3.0 specFile.
- Reading the version to pick a parser does not fix the class of problem. Microsoft.OpenApi has had three incompatible API shapes across 1.6 / 2.x / 3.x, including moving the reader out of a separate package and swapping concrete types for interfaces. A gateway proxies whatever spec an upstream happens to emit, so being version-agnostic is worth more here than typed construction.
- Patching two keys on raw JSON has the property the object model cannot offer: it works on a document it does not understand, including versions that did not exist when this was written.
- If this is ever revisited, the bar is a live check against a real 3.1 upstream, not the unit tests. tools/../swagger-live.sh style: boot examples/EmbeddedGateway, GET /swagger/inventory-service.json, assert components.securitySchemes is non-empty.

# ConduitSharp — Professional Hardening Backlog

Items are ordered by priority within each section.
Each item references the test(s) that must pass (or be un-Skipped) to consider it done.

---

## Security

### S1 — Request body size limit ✅ DONE
**File**: `src/ConduitSharp.Gateway.AspNetCore/Middleware/GatewayMiddlewareExt.cs`
**What**: `ToGatewayRequestAsync()` buffered the entire request body into a `MemoryStream` with no gateway-level cap. (Kestrel's ~28.6 MB transport default bounded a single request, but the limit was not configurable and N concurrent buffered bodies could still exhaust the heap.)
**Fix (implemented)**: `Gateway:RequestLimits` in appsettings — `MaxRequestBodyBytes` (global per-request default, 413 when exceeded, default 8 MiB) and `MaxTotalBufferedBodyBytes` (aggregate across concurrent in-flight bodies via `RequestBodyBudget`, 503 when exhausted, default 128 MiB). Routes can override the per-request limit with `maxRequestBodyBytes` in routes.json (null inherits global, `<= 0` disables for that route); the total budget is always global. Route matching now runs before body buffering, so unmatched requests (404) never buffer a byte. Enforced chunk-by-chunk so chunked bodies are capped too.
**Tests**: `SecurityHardeningTests.RequestBody_ExceedsLimit_Returns413` (un-Skipped, passing) plus global-limit, per-route-override (`RequestBody_ExceedsRouteLimit_Returns413_EvenWhenGlobalAllows`, `RequestBody_RouteLimitRaisesGlobal_LargeBodyIsForwarded`, `RequestBody_RouteLimitZero_DisablesPerRequestCheck`, `RequestBody_UnmatchedRoute_Returns404WithoutBuffering`), and budget tests (`RequestBody_TotalBufferBudgetExceeded_Returns503`, `RequestBody_BudgetIsReleased_SequentialRequestsSucceed`)

---

### S2 — SSRF via Swagger spec `fetchFrom` ✅ DONE
**File**: `src/ConduitSharp.Gateway.AspNetCore.Swagger/SwaggerAggregationExtensions.cs`
**What**: `fetchFrom` URLs were fetched without validation — a misconfigured route (or attacker with config access) could reach internal services (AWS metadata, Redis, etc.).
**Fix (implemented)**: fetch is refused with 403 *before any network I/O* unless the target host is loopback, one of the route's own upstream node hosts, or listed in `Gateway:Swagger:AllowedSpecHosts`. Invalid/relative URLs are refused too.
**Tests**: `SwaggerFetch_PrivateIpRange_IsBlocked` (un-Skipped, passing), `SwaggerFetch_AllowlistedHost_IsAttempted` (new)

---

### S3 — Path traversal in Swagger spec `specFile` ✅ DONE
**File**: `src/ConduitSharp.Gateway.AspNetCore.Swagger/SwaggerAggregationExtensions.cs`
**What**: `specFile` paths were resolved with `Path.GetFullPath` but never checked against `Gateway:BasePath`. `"../../etc/hosts"` resolved and was served.
**Fix (implemented)**: Resolved paths (rooted ones included) must fall under `Gateway:BasePath` (trailing-separator containment check). Violations return 400 with a generic body — the resolved path is never echoed.
**Tests**: `SecurityHardeningTests.SwaggerSpec_PathTraversal_IsBlocked` (un-Skipped, passing)

---

### S4 — JWKS fetch has no timeout ✅ DONE
**File**: `src/ConduitSharp.Security/Jwt/JwksKeyProvider.cs`
**What**: The `HttpClient` fetching JWKS had no per-request timeout. A slow identity provider blocked auth indefinitely, starving the thread pool.
**Fix (implemented)**: `jwksTimeoutMs` on `JwksJwtAuthConfig` (default 5000). The fetch is bounded by a per-request linked `CancellationTokenSource` in `JwksKeyProvider` — chosen over an `HttpClient.Timeout` on the shared named client so the limit is genuinely per-route, and a timeout surfaces as a clear `TimeoutException` → 401. Cache hits are unaffected (no fetch, no timeout).
**Tests**: `JwksJwtAuthEndToEndTests.SlowJwks_ReturnsErrorWithinTimeout`, plus provider units `GetKeyAsync_SlowFetch_ThrowsTimeoutBeforeResponse` / `_FastFetchUnderTimeout_ReturnsKey` and the config default assertion

---

### S5 — Swagger 502 error message leaks internal URLs ✅ DONE
**File**: `src/ConduitSharp.Gateway.AspNetCore.Swagger/SwaggerAggregationExtensions.cs`
**What**: The 502 body included `ex.Message`, which for `HttpRequestException` carries the target URL — leaking internal topology.
**Fix (implemented)**: 502/400/403 bodies are generic (route ID only, which the caller already knows from the request path); the exception detail is logged server-side instead.
**Tests**: `SwaggerFetch_ErrorMessage_DoesNotLeakInternalUrlDetails` (leak assertions now active, passing)

---

### S6 — Route ID used as filesystem directory name without sanitisation ✅ DONE
**File**: `src/ConduitSharp.Core/Routing/GatewayRoute.cs`
**What**: Route IDs are used verbatim as directory names under `PluginsPath` — created AND deleted recursively by `SyncPluginDirectories`, so `"../../evil"` was both a write and a delete primitive.
**Fix (implemented)**: `GatewayRoutesConfiguration.Validate()` rejects IDs containing anything outside `[A-Za-z0-9_-]` (and empty/whitespace IDs) at startup, before any directory I/O.
**Tests**: `SecurityHardeningTests.RouteId_WithPathSeparator_IsRejectedAtStartup` (un-Skipped, passing — test also fixed: it never triggered lazy host startup, so it could not have failed the old code)

---

## Reliability

### R1 — Plugin exceptions crash the pipeline instead of returning 500 ✅ DONE
**File**: `src/ConduitSharp.Gateway.AspNetCore/Middleware/GatewayMiddleware.cs`
**What**: `PluginPipelineExecutor` records the span error and re-throws (correct for tracing), but nothing above caught it — an unhandled plugin exception escaped the middleware with no response written, surfacing as an unobserved exception.
**Fix (implemented)**: `GatewayMiddleware` now catches around match/buffer/execute/proxy. It logs the exception, and when the response has not started, clears and writes a generic 500 (`"Internal server error."` — detail logged, not leaked, per S5). Swallowed after logging so the `finally` records the 500 for observers and the span. The executor's re-throw is unchanged.
**Tests**: `PipelineTests.ThrowingPlugin_Returns500_NotUnobservedException` (500, upstream not called, exception text not in body)

---

### R2 — No upstream retry or circuit breaker ✅ DONE (retry + circuit breaker)
**File**: `src/ConduitSharp.Gateway.AspNetCore/Plugins/HttpProxyPlugin.cs`
**What**: A single timeout or connection error immediately returned 504/502 with no retry. A transient blip permanently failed the request.
**Fix (implemented)**: `retryCount` / `retryDelayMs` on `UpstreamConfig`. The proxy retries transient failures (connection error, upstream timeout, or a 502/503/504 response) in-line, re-selecting a node each attempt (so multi-node routes fail over). Implemented directly rather than via Polly on the shared named client so the policy is genuinely per-route. Retries are gated to idempotent methods (GET/HEAD/OPTIONS/PUT/DELETE/TRACE) — a POST/PATCH may already have been processed upstream. The **circuit breaker** was delivered alongside R3: `NodeHealthTracker` (per-node `circuitBreakerThreshold` / `circuitBreakerCooldownMs` on `UpstreamConfig`) opens a node's circuit after that many consecutive failures, and the load balancer skips it until the cooldown elapses.
**Tests**: `UpstreamErrorTests.TransientUpstreamFailure_IsRetried_ReturnsSuccess`, plus no-retry-configured, retries-exhausted, and non-idempotent-not-retried

---

### R3 — Load balancer does not skip unhealthy nodes ✅ DONE
**File**: `src/ConduitSharp.Traffic/LoadBalancing/`
**What**: `RoundRobinLoadBalancer` and `RandomLoadBalancer` selected nodes without health awareness — a down node was picked every N requests and ate the failure latency each time.
**Fix (implemented)**: shared `NodeHealthTracker` circuit breaker. After `circuitBreakerThreshold` consecutive failures (config on `UpstreamConfig`, default 5) a node's circuit opens and both balancers skip it for `circuitBreakerCooldownMs` (default 10 s), then allow one half-open trial that closes on success or re-opens on failure. `ILoadBalancer` gained `Report(node, success)`; the proxy reports outcomes from the retry loop (a 502/503/504 or transient exception counts against health; a client disconnect does not). If every node's circuit is open the balancer falls back to the full set rather than failing with no target. `threshold <= 0` disables it. Note: the example `YarpProxyPlugin` builds balancers without a tracker, so circuit breaking applies to the built-in proxy path only.
**Tests**: `LoadBalancingTests.DeadNode_IsCircuitBreakerOpenAfterFailures` (unhealthy node hit exactly `threshold` times across many requests), plus `NodeHealthTracker` and health-aware `Pick` units in `LoadBalancerTests`

---

### R4 — Response capture (`CachePlugin`) buffers entire body before streaming ✅ DONE
**File**: `src/ConduitSharp.Gateway.AspNetCore/Plugins/HttpProxyPlugin.cs`
**What**: With `ResponseCaptureCallback` set, `ReadAsStringAsync()` loaded the full upstream body into memory before writing it to the client — defeating streaming for cacheable responses and unbounded in size.
**Fix (implemented)**: `TeeAndCaptureAsync` streams each chunk to the client and captures it in parallel. Capture stops once `PluginContext.ResponseCaptureLimitBytes` is exceeded (set by `CachePlugin` from `maxCacheableBytes`, default 1 MiB) — the client still gets the full body, but oversized responses are streamed without being cached and without a memory spike. `maxCacheableBytes <= 0` means no cap.
**Tests**: `CacheEndToEndTests.LargeResponse_IsCachedWithoutMemoryBlowup` (256 KB body streamed + cached intact) and `ResponseOverCacheLimit_IsStreamedButNotCached` (4 KB body over a 1 KB cap → streamed in full, not cached)

---

### R5 — Graceful shutdown drops in-flight requests ✅ DONE
**File**: `src/ConduitSharp.Host/Program.cs`
**What**: Shutdown (including admin reload, which restarts the process) could interrupt in-flight requests mid-stream.
**Fix (implemented)**: `Gateway:ShutdownTimeoutSeconds` (default 30) surfaced on `GatewayOptions` and applied via `builder.WebHost.UseShutdownTimeout(...)`. Kestrel drains in-flight requests within that window before stopping.
**Tests**: `ShutdownDrainTests` — the configured value (via env var, since the option is read at host-build time) and the 30 s default both flow through to `HostOptions.ShutdownTimeout`

---

### R6 — Rate limiter state is in-memory only ✅ DONE (reversed from WON'T DO)
**File**: `src/ConduitSharp.Traffic/RateLimiting/IRateLimitStore.cs`, `examples/ConduitSharp.RateLimit.RedisProtocol/`
**What**: Rate limit counters are per-process — they reset on restart and are not shared across replicas.
**Resolution**: `IRateLimitStore` is the swap seam (built-in `InMemoryRateLimitStore`, DI-registered in `GatewayServiceCollectionExtensions`). A basic Redis-backed fixed-window store now ships as an OSS drop-in example, `ConduitSharp.RateLimit.RedisProtocol` — same shape as `ConduitSharp.Cache.RedisProtocol` (own project, `Gateway:RateLimiting:Redis:*` config, plugins-directory discovery, fail-open on backend failure). This reverses the earlier "reserved for enterprise" call recorded here; `ENTERPRISE.md` §2 should be revisited so its "Distributed rate limiting" pitch differentiates on sliding-window/token-bucket/leaky-bucket algorithms, per-consumer runtime quotas, and the Admin API rather than basic Redis fixed-window counters, which are no longer enterprise-exclusive.

---

## Operational

### O1 — No health or readiness endpoints ✅ DONE
**File**: `src/ConduitSharp.Host/Program.cs`
**What**: No gateway-owned health endpoint — the LegacyGateway `/health` proxies to an upstream, so it can't report gateway liveness.
**Fix (implemented)**: `/healthz` (liveness, always 200 while the process is up) and `/readyz` (readiness, 200 once a route table is loaded, else 503), answered by the gateway before the terminal middleware so they are never proxied. Deliberately **not** tied to upstream reachability — probing upstreams from readiness pulls every replica out of rotation on a downstream blip (correlated-failure anti-pattern), and it contradicts the "200 independently of upstream" requirement.
**Tests**: `HealthEndpointsTests` — `/healthz` 200 with a dead upstream, `/readyz` 200 with routes, `/readyz` 503 with an empty route table

---

### O2 — Config validation only checks for duplicate route IDs ✅ DONE (structural + per-plugin schema)
**File**: `src/ConduitSharp.Core/Routing/GatewayRoute.cs`, `src/ConduitSharp.Core/Pipeline/PluginPipelineExecutor.cs`
**What**: `Validate()` checked only duplicate IDs.
**Fix (implemented)**: `Validate()` now also asserts, for every route with an upstream: at least one node, `timeoutMs > 0`, every node host uses http/https, and non-negative `retryCount`/`retryDelayMs`. (Malformed URLs already fail at deserialization when `UpstreamNode.Host` binds to `Uri`; route-ID sanitisation and plugin-variant rules were added earlier.) **Per-plugin config-schema validation** was also delivered: `IPipelinePlugin.ValidateConfig(JsonElement)` (default no-op) is invoked for every enabled plugin by `PluginPipelineExecutor.ValidateRouteConfigs` at startup and on admin reload, so plugins such as rate-limit, cache, and jwks-jwt-auth reject invalid config up front with route/plugin context.
**Tests**: `RouteConfigurationTests` — `InvalidUpstreamUrl_ThrowsAtValidation`, `ZeroTimeout_ThrowsAtValidation`, `NegativeRetryCount_ThrowsAtValidation`, `EmptyUpstreamNodes_ThrowsAtValidation`, plus a well-formed pass

---

### O3 — Docker Compose files lack healthcheck blocks ✅ DONE
**File**: `examples/LegacyGateway/docker-compose.yml`, `examples/LegacyGateway/docker-compose.grafana.yml`
**What**: No service declared `healthcheck:`, so `depends_on: condition: service_healthy` could not be used.
**Fix (implemented)**: `healthcheck:` on the four services we own — the gateway (curl `/healthz`, the O1 endpoint) and the three upstreams (curl `/health`; OrderService gained a `/health` to match InventoryService). The gateway now `depends_on` its upstreams with `condition: service_healthy` (and observability deps with `service_started`), so it won't start until they can actually serve. `curl` is installed in the three runtime Dockerfiles (the aspnet image ships without it).
**Scope note:** the third-party observability backends (Aspire, otel-collector, Tempo, Prometheus, Loki, Grafana) are left without healthchecks — most are distroless/shell-less, and a broken healthcheck marking a container permanently unhealthy is worse than none.
**Tests**: verified by the Grafana E2E suite (`make test-e2e-grafana`), which rebuilds the images and brings the stack up under the new healthchecks + `service_healthy` gating; both compose files pass `docker compose config`.

---

### O4 — Admin API route reload is not atomic ✅ DONE
**File**: `src/ConduitSharp.Host/Program.cs`
**What**: The reload wrote the new JSON in place with `File.WriteAllTextAsync`, so a crash mid-write could leave `routes.json` partially written / corrupt.
**Fix (implemented)**: validate the parsed config first, write to a temp file in the same directory, then `File.Move(temp, routesPath, overwrite: true)` — an atomic rename on the same volume, so `routes.json` is never observed partially written. The temp file is cleaned up on any write failure.
**Tests**: `AdminApiTests.Reload_AtomicWrite_LeavesNoTempFiles` (final content correct, no `.tmp-*` left behind)

---

### O5 — No observability on admin API changes (audit trail) ✅ DONE
**File**: `src/ConduitSharp.Host/Program.cs`
**What**: Route reloads left no audit trail — no log, metric, or span event of who reloaded what.
**Fix (implemented)**: on a successful reload the gateway logs a structured event (`Admin route reload applied: {RouteCount} routes from {RemoteIp}`), increments the `conduitsharp.gateway.admin.reloads` counter (new in `GatewayTelemetry`), and adds an `admin.routes.reloaded` span event with route count and client address to the current request activity.
**Tests**: `AdminApiTests.Reload_EmitsAuditReloadCounter` (MeterListener confirms the counter fires once per reload)

---

## How to use this list

1. Pick an item, create a branch named `fix/<id>-<slug>` (e.g. `fix/S1-body-size-limit`).
2. Implement the fix.
3. Un-Skip the corresponding test and confirm it passes.
4. Add any new tests that cover the fix path.
5. Run the full E2E suite: `cd examples/LegacyGateway && make test-e2e`.
6. PR → main.

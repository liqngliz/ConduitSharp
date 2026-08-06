# ConduitSharp architecture

## Capabilities at a glance

Every route gets its own plugin chain from `routes.json`, no code and no recompile. Three routes
below: multi-IDP auth with claim-based RBAC, a distributed cache, YARP forwarding, the PowerShell
plugin, `Custom` extension, and an OTLP sidecar stack.

```mermaid
flowchart TB
    client(["Client / caller"])
    client -->|HTTPS| router
    client -.->|/swagger| swaggerui

    subgraph gw ["ConduitSharp Gateway — routes.json, no code changes"]
        router["ASP.NET Core endpoint routing\nfirst-match-wins by declaration order\npath · method · header · query"]
        swaggerui["Aggregated Swagger UI\n(optional add-on package)"]

        subgraph route1 ["/api/orders — REST, scaled out"]
            direction LR
            r1auth["jwt-auth / jwks-jwt-auth\nAzure AD · Auth0 · Okta · Keycloak\n(any number of IDPs, one per route)"]
            r1rbac["requiredClaims RBAC\nwrong role → 403, not 401"]
            r1rl["rate-limit\nin-memory, or Redis/Valkey drop-in\nfor shared limits across instances"]
            r1cache["cache\nin-memory, or Redis/Valkey drop-in\nfor shared cache across instances"]
            r1fwd["http-proxy\nor YARP plugin — HTTP/2, gRPC, WebSocket"]
            r1auth --> r1rbac --> r1rl --> r1cache --> r1fwd
        end

        subgraph route2 ["/erp/reports — legacy modernization"]
            direction LR
            r2auth["jwt-auth"]
            r2rbac["requiredClaims RBAC"]
            r2ps["power-shell plugin\nruns the existing .ps1 in-process\nno rewrite of the script"]
            r2auth --> r2rbac --> r2ps
        end

        subgraph route3 ["/anything — your own extension"]
            direction LR
            r3auth["api-key-auth\nor a custom auth plugin"]
            r3custom["PluginName.Custom + Variant\ndrop-in DLL, zero gateway rebuild"]
            r3auth --> r3custom
        end

        router --> route1
        router --> route2
        router --> route3
    end

    r1fwd --> upstream[("REST / gRPC\nupstream services")]
    r2ps --> legacy[("Existing script, COM,\nOLEDB → on-prem ERP")]
    r3custom --> anything[("DB, SaaS API, WMI —\nanything HTTP can reach")]

    gw -. OTLP traces + metrics + logs .-> sidecar

    subgraph sidecar ["Sidecar observability stack — swap freely, same OTLP contract"]
        direction LR
        aspire["Aspire Dashboard"]
        grafanastack["Grafana + Tempo + Prometheus + Loki"]
    end
```

| Capability | How |
|---|---|
| **Auth + RBAC** | `jwt-auth` (HS256) and `jwks-jwt-auth` (RS/ES via JWKS), both taking an optional `requiredClaims` block. A different IDP per route is a different config block, no code. Failed claim check `403`, bad or expired token `401`. See [Claim-based authorization](AUTHORIZATION.md#claim-based-authorization-rbac). |
| **Expandability** | Any built-in plugin (auth, cache, forwarding) is replaceable by a drop-in DLL under the same `PluginName` (last-registration-wins). `PluginName.Custom` adds new plugin types with no `Core` recompile. |
| **Cache** | In-memory by default. Drop `ConduitSharp.Cache.RedisProtocol` into `plugins/` for one cache across every instance, with request coalescing and per-route invalidation. |
| **Rate-limit store** | Per-process fixed window by default. Drop `ConduitSharp.RateLimit.RedisProtocol` into `plugins/` for one shared limit across every instance (same `IRateLimitStore` seam, fail-open on backend outage). |
| **Route matching** | Native ASP.NET Core endpoint routing, DFA-based. |
| **Load-balancing policy** | YARP's `RoundRobin`, `Random`, `PowerOfTwoChoices`, `LeastRequests`, `FirstAlphabetical` by name. Drop an `ILoadBalancingPolicy` DLL into `plugins/` and select it with `cluster.loadBalancingPolicy`. |
| **Forwarding engine** | YARP `IHttpForwarder`: HTTP/2, gRPC, WebSockets, streaming, trailers, on every route. Not a plugin and not swappable; `http-proxy` in a route's plugin list names where in the chain the forward happens. |
| **Legacy modernization** | `power-shell` runs an existing `.ps1` in-process, so a script with no API surface becomes an authenticated, observable HTTP endpoint with its logic untouched. |
| **Observability** | Every instance emits the same OTLP traces, metrics, and logs whatever collects them. Swap Aspire for Grafana/Tempo/Prometheus/Loki, Jaeger, Datadog, or Honeycomb without touching the gateway. |

---

## Deployment patterns

### Edge gateway (standard)

One instance at the perimeter, all external traffic through it. Auth, rate limiting, and routing
enforced at a single boundary.

### Sidecar / terminating wrapper

One small instance next to each workload (legacy service, PowerShell ETL job, batch processor),
enforcing the same auth and observability locally whatever the workload speaks.

ConduitSharp does not have to be the edge. It slots behind Azure API Management, APISIX, or any
existing gateway and adds the observability and execution layer those cannot give legacy workloads.

```mermaid
sequenceDiagram
    participant Client
    participant Edge as Azure APIM / APISIX
    participant CS as ConduitSharp (internal)
    participant Inv as inventory-svc
    participant PS as PS report script

    Client->>Edge: request (external auth, throttling, TLS termination)
    Edge->>CS: forward request<br/>traceparent f3a9b1... propagated
    par inventory check
        CS->>Inv: GET /inventory/123
        Inv-->>CS: 200 OK (45ms)
    and PS report generation
        CS->>PS: POST /reports/generate → Get-Report.ps1
        PS-->>CS: 502 (3001ms, script timeout)
    end
```

Both calls carry the same `traceparent`; all spans flow to the OTLP collector. In Grafana Tempo the
waterfall correlates under one trace ID:

```
Trace f3a9b1                                        3050ms  ✗ ERROR
  ├─ [inventory-check]  GET /inventory/123            45ms  ✓ 200
  └─ [ps-erp-wrapper]   POST /reports/generate      3001ms  ✗ 502
       route: erp-report-generate
       conduitsharp.route_id: erp-report-generate
       http.response.status_code: 502                         ← source of failure
```

The edge gateway sees a 502. ConduitSharp names *which* downstream failed, on *which* route, after
*how long*, without touching the PowerShell script or the inventory service.

Each instance runs independently with its own `routes.json`. The legacy workloads do not know it
exists.

### Domain gateway per Data Product

Scope one instance to one Data Product's namespace, the 5-8 microservices it owns, instead of one
instance per edge or per workload. Each Data Product gets its own instance, `routes.json`, and
namespace. One edge gateway (Azure API Management, APISIX) sits above all of them, handling
external auth, throttling, and TLS termination for the estate.

```mermaid
flowchart TB
    client(["External callers"]) --> edge

    subgraph edge ["Edge — Azure APIM / APISIX"]
        edgenote["external auth, throttling, SSL termination\npropagates W3C traceparent downstream"]
    end

    edge --> gwA
    edge --> gwB

    subgraph gwA ["ConduitSharp — ERP Data Product\nnamespace: erp"]
        rA["routes.json — 5-6 routes"]
    end
    subgraph gwB ["ConduitSharp — Identity Data Product\nnamespace: identity"]
        rB["routes.json — 5-8 routes"]
    end

    rA --> svcA1["erp svc 1"] & svcA2["erp svc 2"] & svcAn["... up to ~6"]
    rB --> svcB1["identity svc 1"] & svcB2["identity svc 2"] & svcBn["... up to ~8"]
```

Per-Data-Product rather than one shared gateway: each team owns a `routes.json` sized to what it
runs and reviews it without wading through every other team's routes. The edge keeps one external
contract, one place for org-wide policy, and one aggregation point for client-facing auth. Every
instance emits the same OTLP traces (below), so a request crossing the edge into a Data Product
gateway stays one correlated trace.

### Single observability plane across all instances

Every instance emits OTLP traces and metrics to the same collector. `AddHttpClientInstrumentation`
injects the W3C `traceparent` header on every forwarded request, so spans from different instances
share a trace ID across one request chain.

In Grafana Tempo, one waterfall shows every hop:

```
Trace: a3f8c1...

  edge gateway           GET /erp/reports/summary    12ms   OK
    │
    └─ PS ERP wrapper    GET /erp/reports/summary   450ms   ERROR  502
```

**Visible per hop:**
- Which route matched, on which instance
- Status code and latency
- Plugin short-circuit (auth rejection, rate limit) vs an upstream/script failure
- The chain from edge to terminal workload, correlated by trace ID

**Not visible:**
- Steps inside a PowerShell script. The span covers the whole execution; finer granularity needs
  the script to emit its own spans.
- Event bus hops (Kafka, Service Bus). HTTP only: it can sit in front of the webhook consumer the
  bus triggers, but the queue transit is invisible.

### Horizontal scaling and in-memory state

Rate limiting and response caching are **in-memory and per-process** by default, so behind a load
balancer each instance keeps its own window and its own cache. Drop
`ConduitSharp.RateLimit.RedisProtocol` or `ConduitSharp.Cache.RedisProtocol` into the plugins root
for one shared limit and one shared cache across instances, both fail-open. Setup and semantics:
[Observability](OBSERVABILITY.md#horizontal-scaling-and-state).

Single-instance deployments (Windows Service, single container, IIS on one node) run on the
in-memory cache with no external dependency.

---

## Request lifecycle

Wired by `UseConduitSharpGateway()` in `ConduitSharp.Gateway.AspNetCore`, so the standalone Host,
the Docker image, and any embedding app share it. Each `Use()` stage toggles individually through
`ConduitSharpGatewayOptions` when embedding.

```
HTTP request
  → Kestrel
  → Admin middleware (Use()) — POST /admin/routes/reload, DELETE /admin/cache/{routeId}
                               if Gateway:AdminKeyHash is set          [EnableAdminApi]
  → Health middleware (Use()) — /healthz (liveness), /readyz (readiness)  [MapHealthEndpoints]
  → SwaggerUI + spec proxy middleware — /swagger/**    [optional add-on package:
                                                        app.UseConduitSharpGatewaySwagger()]
  → OTel ActivitySource.StartActivity("gateway.request")   [wraps every gateway request,
                                                           including ones that match no route]
  → ASP.NET Core endpoint routing   native DFA over path + method + header + query
                                    (route list order → RouteConfig.Order, so declaration
                                     order breaks overlaps: first match wins)
      → 404 if no route matched, 405 if the path matched but the verb did not

  ── route has an upstream → YARP's proxy pipeline (MapReverseProxy) ──
      → plugin dispatch    looks the route's precompiled RequestDelegate up by RouteId
          → sets Items["ConduitSharp.RouteId"] (a string) and Items["ConduitSharp.ProxyNext"]
          → BufferRequestBody      per-route/global body cap (413) + total budget (503);
                                   leaves a seekable body the retry loop can rewind
          → ordered plugin chain   (auth → rate-limit → cache → transform → …)
              → a plugin that writes a response and does not call next() short-circuits;
                YARP never forwards
              → terminal step invokes ProxyNext — i.e. the rest of YARP's pipeline runs
                INSIDE the plugins' next(), so the cache plugin's tee wraps the real forward
      → UpstreamProtocol   inbound HTTP/2 → swap in an h2c prior-knowledge cluster model (gRPC)
      → UpstreamRetry      Polly-driven attempt loop (idempotent methods, or non-idempotent when the route opts in):
                             rewind body → restore destination set → forward → judge outcome
                             a retryable attempt's response is suppressed, never reaching the client
      → UseLoadBalancing        picks a destination per attempt (RoundRobin by default)
      → UsePassiveHealthChecks  ConsecutiveFailuresHealthPolicy — circuit breaker
      → ForwarderMiddleware     IHttpForwarder.SendAsync → streams headers, body, trailers back
                                504 on timeout, 502 on transport failure

  ── route has no cluster ("cluster": null) → plain endpoint ──
      → same precompiled chain; terminal step is a 502 if no plugin produced a response

  → finally: release the body budget; tag the activity with the status code, mark Error if 5xx
```

---

## Package structure and dependency graph

```
src/
  ConduitSharp.Core                    ← contracts + pure domain logic; NO ASP.NET dependency
  ConduitSharp.Traffic                 → Core   (rate limiting, caching, load balancers)
  ConduitSharp.Security                → Core   (JWT HS256, JWKS RS/ES, API key plain+hashed)
  ConduitSharp.Transformation          → Core   (header-transform plugin)
  ConduitSharp.Observability           → Core   (logging, metrics, OTel instruments, IRequestObserver)
  ConduitSharp.Gateway.AspNetCore      → Core + all feature packages  (embeddable library:
                                          AddConduitSharpGateway / UseConduitSharpGateway —
                                          middleware, proxy engine, plugin loader, options)
  ConduitSharp.Gateway.AspNetCore.Swagger → Gateway.AspNetCore  (optional add-on: aggregated
                                          Swagger UI; carries the Swashbuckle dependency)
  ConduitSharp.Host                    → Gateway.AspNetCore + Swagger add-on  (thin executable
                                          shell that dogfoods the library; ships as the
                                          `ConduitSharp.Gateway` dotnet tool and Docker image)

tests/
  ConduitSharp.Core.Tests          → Core
  ConduitSharp.Security.Tests      → Security
  ConduitSharp.Traffic.Tests       → Traffic
  ConduitSharp.Transformation.Tests → Transformation
  ConduitSharp.Observability.Tests → Observability
  ConduitSharp.Integration.Tests   → Host  (in-process via WebApplicationFactory; also
                                     exercises the embeddable surface on a TestServer)
  ConduitSharp.LegacyGateway.E2E.Tests → out-of-process E2E against the real gateway binary
  ConduitSharp.Grafana.E2E.Tests   → Docker observability-pipeline E2E (Tempo/Prometheus/Loki)
  ConduitSharp.Mtls.E2E.Tests      → Docker mTLS E2E (real client-cert handshake; cross-platform)

examples/                            — runnable demos, not shipped as packages
  EmbeddedGateway/                   — embeds the gateway in a plain ASP.NET Core app and adds
                                       the Redis cache plugin from NuGet, in code
  LegacyGateway/                     — runnable multi-route demo stack (make run / start.ps1)

plugins/                             — drop-ins, each src/ + tests/, published to NuGet
  ConduitSharp.Plugin.PowerShell/    — PluginName.PowerShell drop-in that runs a .ps1
                                       in-process via the embedded Microsoft.PowerShell.SDK
  ConduitSharp.Plugin.BodyCapture/   — bounded request-body prefix logging without forcing a buffer
  ConduitSharp.Plugin.BodyCaptureToFile/ — the same capture, written to a file sink
  ConduitSharp.Plugin.TokenRateLimit/    — meters LLM tokens per caller over the rate-limit store
  ConduitSharp.Cache.RedisProtocol/  — drop-in distributed ICacheService (Valkey / Redis 7 / RESP)
  ConduitSharp.RateLimit.RedisProtocol/ — drop-in distributed IRateLimitStore (shared limits, fail-open)
  ConduitSharp.RateLimit.SlidingWindow/ — drop-in IRateLimiter, sliding log instead of fixed window
```

**Rule:** feature packages reference only `Core`, never each other or the gateway library.
`ConduitSharp.Gateway.AspNetCore` is the single aggregation point; `Host` adds nothing but the
executable entry point and its `Configuration/` packaging convention. All `ConduitSharp.*`
packages version together from a single `<Version>` in the root `Directory.Build.props`.

---

## Key architectural decisions

### The gateway is an embeddable library; the Host is a thin shell

All wiring lives in `ConduitSharp.Gateway.AspNetCore` as two extension methods on YARP's
`AddReverseProxy()`/`MapReverseProxy()` model: `AddConduitSharpGateway(builder, options => …)`
(config binding, HttpClients, plugins, routes, observability) and `UseConduitSharpGateway(app)`
(admin, health, terminal middleware). The standalone Host executable is ~40 lines that add its
`Configuration/appsettings.json` convention and call both; the Docker image and dotnet tool package
that same shell.

`ConduitSharpGatewayOptions` exposes the composition knobs an embedder needs so the gateway does
not collide with an app that already owns those concerns: `ConfigureObservability` (don't clobber
the host's OTel), `EnablePluginDirectoryScan` (plugins via DI instead of a folder),
`EnableAdminApi` (reload calls `StopApplication()`, wrong for a shared process),
`MapHealthEndpoints`, `PathPrefix` (mount the terminal middleware under a sub-path via `UseWhen`
so the host keeps the rest of its routing), and `Routes`/`RoutesPath` (in-memory route table or a
file). Defaults reproduce the standalone host exactly. Because the pipeline is resolved from DI
after `Build()`, plugins registered *after* `AddConduitSharpGateway` are picked up, which is what
makes one-line NuGet plugin registration work (see `examples/EmbeddedGateway`).

### PluginName is a strict enum; extensibility lives in `Custom` + `Variant`

`PluginName` is a closed enum (`JwtAuth`, `RateLimit`, `Cache`, `HttpProxy`, …) deserialized
strictly from kebab-case JSON via `StrictEnumConverter<T>`; an unrecognised name in `routes.json`
throws at load time, so typos fail fast rather than surfacing per request.
`LoadBalancingStrategy` and `HttpVerb` follow the same pattern.

Open extensibility comes from two enum members plus a disambiguator:

- **`Custom`**: third-party plugins declare `Name => PluginName.Custom` with a self-chosen
  `Variant` string (e.g. `"llm-proxy"`); routes select them with
  `{ "name": "custom", "variant": "llm-proxy" }`. Plugins are registered and resolved under a
  `PluginKey` (name + case-folded variant), so any number of custom plugins coexist without
  each needing a core enum value. Config validation enforces the pairing at startup: `custom`
  requires a variant, built-in names must not carry one.
- **`PowerShell`**: a reserved escape hatch with no built-in implementation.

A built-in name can also be *taken over* wholesale: a plugin declaring an existing name (null
variant), like the YARP example's `PluginName.HttpProxy`, replaces the built-in via
last-registration-wins. One failure mode stays deferred by design. A route naming a plugin with no
registered implementation (e.g. `custom`/`power-shell` with no DLL supplying it) is skipped by
startup config validation and surfaces as a 500 when the route's chain is first built, because the
registry can legitimately gain entries only after route validation runs.

### Plugins are native middleware over HttpContext

`IPipelinePlugin.ExecuteAsync(HttpContext, JsonElement config, RequestDelegate next)` is the ASP.NET
Core middleware shape, no custom context types. Each route's chain is compiled once (at startup and
on reload) via `IApplicationBuilder.New()` into a single `RequestDelegate`, ordered by `Order`
ascending with the lowest order outermost. The only gateway-specific contract is
`HttpContext.Items`: `"ConduitSharp.RouteId"` (the matched route's id, a string) and
`"ConduitSharp.ProxyNext"` (the continuation into YARP's pipeline, invoked by the chain's terminal
step). Plugins are unit-testable with a bare `DefaultHttpContext`.

### Last registration wins (override mechanism)

Plugins resolve from DI as `IEnumerable<IPipelinePlugin>` with a `LastOrDefault` match on
(name, variant). Registration order (in `AddConduitSharpGateway`) is: built-ins first, then
external DLLs scanned from `plugins/`, then, when embedding, anything the host app registers in DI
*after* `AddConduitSharpGateway`. Later registrations win, so an external DLL, a NuGet plugin
registered in code, or a test plugin can replace any built-in by using the same plugin name (how
`examples/EmbeddedGateway` swaps in the Redis cache with one `AddSingleton` line).

### Forwarding is health-aware and retryable

`timeoutMs` becomes the cluster's `ForwarderRequestConfig.ActivityTimeout`, bounding each
*attempt* rather than the whole request; YARP maps a timeout to 504 and a transport failure to
502, so a timeout stays distinguishable from a client disconnect.

Idempotent methods (`GET`, `HEAD`, `OPTIONS`, `PUT`, `DELETE`, `TRACE`) retry per the route's
`retry` policy; a `POST`/`PATCH` never retries, since it may already have been applied upstream.
Attempts are scheduled by a per-route Polly `ResiliencePipeline` (`Fixed`/`Linear`/`Exponential`
backoff, optional jitter, configurable `retryOn` status set). The loop owns what Polly cannot see:
it rewinds the buffered request body, restores the full destination set so YARP's load balancer
re-picks (giving cross-node failover), and resets the status and headers a failed attempt left
behind. A retried attempt never reaches the client: `SuppressRetriedResponseTransform` holds its
body back while another attempt remains, so the forwarder returns without starting the response.

Every attempt's outcome feeds `ConsecutiveFailuresHealthPolicy`, a YARP `IPassiveHealthCheckPolicy`:
after `circuitBreaker.threshold` consecutive failures the destination is marked unhealthy and drops
out of the load balancer's available set for `circuitBreakerCooldownMs` (the reactivation period),
after which one trial request decides whether it resets or re-opens. A client disconnect is never
counted as a node failure. An absent `circuitBreaker` block disables it (passive health checks are not
enabled on the cluster at all).

### Per-plugin config validation runs at startup, not per request

`IPipelinePlugin.ValidateConfig(JsonElement)` defaults to a no-op; a plugin overrides it
to reject invalid config early (`rate-limit` requires a positive window/max, `cache` a
positive ttl, `jwks-jwt-auth` a `jwksUri`). `PluginPipelineExecutor.ValidateRouteConfigs`
calls it for every enabled route/plugin at startup and on admin reload, wrapping failures
with route/plugin context so a bad value fails the deploy or reload instead of the first
request that happens to hit it. Separate from a plugin's own `static From(JsonElement)`
config-loading convention, which still runs per request in `ExecuteAsync`; `ValidateConfig` is the
opt-in fail-fast layer on top.

### Forwarding is YARP, and the plugin chain runs inside it

routes.json is translated at startup into YARP `RouteConfig`/`ClusterConfig` pairs (one cluster per
route, `ClusterId == RouteId`) served through an `InMemoryConfigProvider`. YARP's own appsettings
schema is never bound; routes.json stays the only config surface.

Each route's plugin chain is compiled once into a `RequestDelegate` and dispatched *inside* YARP's
per-proxy pipeline, so the forwarder always runs within the plugins' `next()`. That ordering makes
the cache plugin's response tee wrap the real forward (coalescing always engages) and lets any
plugin short-circuit by not calling `next()`.

`http-proxy` in a route's plugin list is not a plugin; it names where in the chain the forward
happens. Omit it and the forward is appended at the end. It is deliberately *not* swappable:
protocol fidelity (HTTP/2, gRPC, WebSockets, trailers) is the whole reason to stand on YARP.

What YARP does not do, the gateway keeps around the forwarder:

- **Retries.** A proxy cannot safely replay a half-streamed body. The gateway buffers the request,
  drives attempts from a per-route Polly `ResiliencePipeline` (backoff, jitter), rewinds the body,
  and re-runs load balancing per attempt so a retry lands on a different node. A retried attempt's
  response is suppressed before it reaches the client (`SuppressRetriedResponseTransform`).

  Every gateway that retries must buffer the body somewhere, since a sent stream no longer has the
  bytes a replay needs. They differ in what happens when the buffer does not fit:

  | Gateway | Retryable body lives in | When it does not fit |
  |---|---|---|
  | ConduitSharp | RAM budget, then disk budget | `503` when neither has room |
  | Envoy | memory only (`request_body_buffer_limit`) | `507`, retry abandoned |
  | nginx / APISIX | `client_body_buffer_size`, then a temp file | unbounded spill |
  | Ocelot | nothing shipped; a retry built on its official `AddPolly` seam must `LoadIntoBufferAsync` | whole body on the heap, no ceiling |
- **Circuit breaking.** `ConsecutiveFailuresHealthPolicy`, an `IPassiveHealthCheckPolicy`. YARP's
  stock `TransportFailureRate` is rate-over-a-window and cannot express routes.json's
  consecutive-failure threshold; cooldown maps to the reactivation period.
- **Per-route mTLS.** `UpstreamForwarderHttpClientFactory` attaches the route's client certificate
  to the cluster's `SocketsHttpHandler`. YARP's `HttpClientConfig` covers
  `skipCertificateVerification` but has no client-certificate knob.
- **HTTP/2 prior knowledge.** A cluster's `ForwarderRequestConfig` is static, while the right
  outbound protocol depends on how the client arrived. For an inbound HTTP/2 request
  `UpstreamProtocol` swaps in an h2c-exact cluster model, so gRPC is not silently downgraded to
  HTTP/1.1 against a cleartext upstream.

### Routes without an upstream bypass YARP

YARP rejects a route with no cluster before any middleware runs, so plugin-only routes
(`"cluster": null`) are served as ordinary endpoints from a mutable `EndpointDataSource` running
the same compiled chain, which is also what lets an admin reload add and remove them.

### Response capture is a Body swap inside the plugin's own next()

The cache plugin needs the response (status, content-type, bytes) to store it, but responses are
*streamed*, so capturing means teeing bytes as they flow to the client. The tee swaps
`context.Response.Body` for a `CapturingStream` before calling `next()` and restores it after.

That works, YARP included, because of two facts:

1. Setting `Response.Body` makes ASP.NET Core install a `StreamResponseBodyFeature` over the new
   stream, which routes **both** write surfaces through it: `Response.Body` (a `Stream`) *and*
   `Response.BodyWriter` (the `PipeWriter` YARP's `IHttpForwarder` writes to). YARP cannot bypass
   the swap. (A plugin that captured a raw `IHttpResponseBodyFeature` reference *before* the swap
   could; nothing in this codebase does.)
2. The forward runs *inside* the plugins' `next()` (the chain's terminal step invokes YARP's
   pipeline), so the swap is always in place while the forwarder writes, and coalescing (the leader
   publishing the captured body to concurrent misses) always engages.

Only 2xx responses within `maxCacheableBytes` are stored; an oversized body streams to the client
uncaptured. `CacheEndToEndTests` proves the whole path against a real YARP forward
(`X-Cache: HIT`, `ConcurrentMisses_AreCoalesced_UpstreamCalledOnce`).

### Gateway-owned endpoints are Use() middleware, routes are endpoints

Admin (`/admin/*`), health (`/healthz`, `/readyz`), and the Swagger add-on are plain `Use()`
middleware registered by `UseConduitSharpGateway()` (Swagger's `UseConduitSharpGatewaySwagger()`
must be called before it). They answer and return before endpoint routing runs, so no routes.json
route can shadow them. Everything in routes.json becomes a real endpoint (YARP's or the plugin-only
data source's), which is what makes 404/405 semantics, `MatcherPolicy` drop-ins, and hot reload
native.

### No assembly isolation: plugins share the host's load context

Every DLL in `plugins/` loads into `AssemblyLoadContext.Default` with full trust. Isolated
(collectible) contexts were abandoned deliberately: they break plugins using native P/Invoke (e.g.
the PowerShell SDK's `libpsl-native`). A `Resolving` handler resolves each plugin's private
dependencies from the files published alongside its DLL, and assemblies the host already loaded are
reused, so the plugin's `IPipelinePlugin` has the same type identity as the host's. Security model:
plugins run in-process with the gateway's full privileges, so deploy DLLs only from sources you
control.

Scoping. Plugin *activation* is per-route (each route's `plugins` list in routes.json picks which
plugins run: auth, rate-limit, cache, the forwarder, each with per-route config). DLL *discovery* is
gateway-wide: the `plugins/{routeId}/` folders only organize files, and every discovered plugin type
is registered globally by (name, variant), available to any route that declares it. Service backends
dropped in the plugins root (`ICacheService`, `IRateLimitStore`, YARP `ILoadBalancingPolicy`,
ASP.NET Core `MatcherPolicy`) are instance-wide and apply to all routes.

### One HttpMessageInvoker per cluster, built by YARP

Upstream forwarding no longer uses `IHttpClientFactory`. YARP builds one `HttpMessageInvoker` per
cluster (= per route) from the cluster's `HttpClientConfig`, so
`skipCertificateVerification` maps straight to `DangerousAcceptAnyServerCertificate`.

`HttpClientConfig` has no *client*-certificate knob, so `UpstreamForwarderHttpClientFactory`
(an `IForwarderHttpClientFactory`) attaches the route's certificate to the cluster's
`SocketsHttpHandler`. Clusters are keyed by route id, so a certificate configured for
`Gateway:Tls:ClientCertificates[n]:RouteId` lands on the matching cluster. Certificates load when
YARP builds the client at config load, so a bad path or thumbprint fails the gateway at startup, not
on the first request.

The insecure client carries **no client certificate**, so configuring both a client cert and
`skipCertificateVerification` on one route would silently drop the cert. `AddConduitSharpGateway`
rejects the combination at startup with a clear error instead. For a self-signed upstream that also
requires mTLS, trust its CA (e.g. `SSL_CERT_FILE` on Linux); see
[tests/ConduitSharp.Mtls.E2E.Tests/assets](../tests/ConduitSharp.Mtls.E2E.Tests/assets).

### Swagger aggregation is an optional add-on package

The aggregated Swagger UI lives in `ConduitSharp.Gateway.AspNetCore.Swagger`, outside the core
library, so embedders who skip it never take the Swashbuckle dependency. One extension method,
`app.UseConduitSharpGatewaySwagger()` called before `UseConduitSharpGateway()`, resolves the route
table from DI and mounts the spec-proxy + UI middleware. The standalone Host references the add-on,
so `/swagger` works out of the box in the tool and Docker distributions.

### Hop-by-hop headers stripped before forwarding

`BuildUpstreamRequest` removes `Connection`, `Keep-Alive`, `Transfer-Encoding`, and related headers. The `Host` header is also removed so the upstream sees its own hostname.

### First-match-wins routing

Route priority is position in `routes.json`. No scoring, no specificity algorithm. Put more-specific
routes before catch-alls; list order is the contract.

A most-specific-path-wins algorithm is more convenient to author and opaque to review, since you
cannot tell what a request matches without running the algorithm in your head. First-match-wins over
a flat JSON file means the table reads top to bottom, in order, which is what a security audit needs
to answer "what can reach this service, through what auth, in what plugin order". Each instance's
`routes.json` stays small for the same reason (see
[Domain gateway per Data Product](#domain-gateway-per-data-product)).

### Static routing: dynamic node discovery is a non-goal

No Consul / Kubernetes / DNS service-discovery integration, no control plane. Routes *and* upstream
nodes are a version-controlled file:

- **Auditability.** Git history answers who changed a route, when, and to what. A dynamic registry
  reports current state only; reconstructing history means trusting a second system.
- **Zero drift.** Production matches the repo. No route or node appears via a runaway process or a
  manual poke at a live store.
- **Operational simplicity.** A stateless appliance: no etcd/Consul quorum to operate, back up, or
  repair, and a failed node is replaced rather than fixed.

Node changes flow through the same reviewed pipeline as code (edit `routes.json` → PR →
`POST /admin/routes/reload` or redeploy). Deployments needing dynamic upstreams replace the
forwarding plugin (`PluginName.HttpProxy`, last-registration-wins, the mechanism the YARP example
uses) with one that resolves nodes itself; that choice lives visibly in the deployment's plugin set
instead of silently in the core.

---

## Implementation status

| Package | Status |
|---|---|
| `Core` | **Fully implemented**. routing, pipeline executor, all types |
| Load balancing | **YARP**. `RoundRobin` (default), `Random`, `PowerOfTwoChoices`, `LeastRequests`, `FirstAlphabetical`, or a drop-in `ILoadBalancingPolicy` DLL, selected by `cluster.loadBalancingPolicy` |
| `Traffic`: rate limiting | **Implemented**. `RateLimitPlugin`, `FixedWindowRateLimiter` (injectable clock, eviction), `ValidateConfig` rejects non-positive window/max |
| `Traffic`: caching | **Implemented**. `CachePlugin`, `InMemoryCacheService`, `ValidateConfig` rejects non-positive ttl |
| `Security`: JWT (HS256) | **Implemented**. `JwtAuthPlugin`, `JwtAuthHandler`, `JwtClaimsValidator`, `JwtBase64Url` |
| `Security`: JWT (RS/ES via JWKS) | **Implemented**. `JwksJwtAuthPlugin`, `JwksJwtAuthHandler`, `JwksSignatureVerifier`, `JwksKeyProvider`, `ValidateConfig` requires `jwksUri` |
| `Security`: API key (plain) | **Implemented**. `ApiKeyAuthPlugin`, `ApiKeyAuthHandler` |
| `Security`: API key (hashed) | **Implemented**. `ApiKeyAuthHashedPlugin`, `ApiKeyAuthHashedHandler` |
| `Transformation`: header-transform | **Implemented**. `HeaderTransformPlugin` (add, remove, set) |
| `Observability`: structured logging | **Implemented**. `StructuredRequestLogger`, `StructuredLogEntry` |
| `Observability`: OpenTelemetry | **Implemented**. `GatewayTelemetry` (ActivitySource + Meter), `OtelMetricsObserver` |
| `Gateway.AspNetCore`: embeddable library | **Implemented**. `AddConduitSharpGateway` / `UseConduitSharpGateway` + `ConduitSharpGatewayOptions` composition knobs; ships as a NuGet package |
| `Gateway.AspNetCore`: request pipeline | **Implemented**. OTel tracing, body limits/budget, per-route compiled plugin chains, hot reload (`GatewayRouteTable`) |
| `Gateway.AspNetCore`: forwarding | **YARP `IHttpForwarder`**. HTTP/2, gRPC, WebSockets, streaming, trailers. Around it: `YarpConfigTranslator` (routes.json → route/cluster config), `UpstreamRetry` (Polly-driven, idempotent by default / opt-in non-idempotent, body rewind, cross-node failover), `ConsecutiveFailuresHealthPolicy` (circuit breaker), `UpstreamForwarderHttpClientFactory` (per-route mTLS), `UpstreamProtocol` (h2c prior knowledge) |
| `Gateway.AspNetCore`: admin API | **Implemented**. `POST /admin/routes/reload`, `DELETE /admin/cache/{routeId}`, gated by `Gateway:AdminKeyHash` |
| `Gateway.AspNetCore`: plugin loader | **Implemented**. `PluginAssemblyLoader` (loads into the shared default `AssemblyLoadContext`; no isolation) |
| `Gateway.AspNetCore.Swagger`: add-on | **Implemented**. `UseConduitSharpGatewaySwagger()`; `SwaggerAggregationExtensions` (fetchFrom + specFile modes, SSRF/path-traversal guards) |
| `Host`: standalone shell | **Implemented**. thin executable over the library; dotnet tool (`conduitsharp`) + arch-portable Docker image (amd64/arm64) |
| `Custom` | **No core-provided implementation**. `PluginName.Custom` + a self-chosen `Variant` is the open-ended escape hatch; implement as a drop-in DLL |
| `PowerShell` | **Example implementation**. `plugins/ConduitSharp.Plugin.PowerShell` runs a `.ps1` in-process via the embedded PowerShell SDK; see [PowerShell plugin: production considerations](#powershell-plugin-production-considerations) before heavy concurrent/ETL use |
| `examples/EmbeddedGateway` | **Example**. embeds the gateway in a plain ASP.NET Core app; wires the Redis cache plugin from NuGet in code |
| `examples/PowerShell` | **Example**. `ConduitSharp.Plugin.PowerShell`: in-process `.ps1` execution via `Microsoft.PowerShell.SDK`, no system `pwsh` required |
| `examples/Cache.RedisProtocol` | **Example**. drop-in distributed `ICacheService` over RESP (Valkey / Redis 7) |
| `examples/RateLimit.RedisProtocol` | **Example**. drop-in distributed `IRateLimitStore` over RESP; one shared limit across all instances, fail-open |

---

## PowerShell plugin: production considerations

`plugins/ConduitSharp.Plugin.PowerShell` is a working drop-in (`PluginName.PowerShell`) that runs a
`.ps1` in-process via the embedded `Microsoft.PowerShell.SDK` (no system `pwsh` install) and
short-circuits with the script's output. It uses the same `PowerShell.Create()`-per-request shape as
the shim below, which illustrates the pattern but does not survive concurrent load or heavy ETL.
Three concerns before deploying either at scale.

### 1. Runspace pooling

`PowerShell.Create()` per request builds a new runspace on every call. SDK assemblies load once and
are shared in process memory, so the cost is not 50MB per request; it is GC pressure from
short-lived runspace objects plus no ceiling on concurrency. Under load: GC pause spikes,
unpredictable latency.

A `RunspacePool` sized to expected concurrency caps memory growth, drops repeated initialisation,
and bounds concurrent executions. Same principle as a database connection pool.

```csharp
public sealed class PowerShellPlugin : IPipelinePlugin, IDisposable
{
    private readonly RunspacePool _pool;

    public PowerShellPlugin()
    {
        var state = InitialSessionState.CreateDefault();
        _pool = RunspaceFactory.CreateRunspacePool(minRunspaces: 2, maxRunspaces: 20, state, host: null);
        _pool.Open();
    }

    public PluginName Name => PluginName.PowerShell;

    public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
    {
        var options = JsonSerializer.Deserialize<PsConfig>(config)
            ?? throw new InvalidOperationException("PowerShell plugin config is null.");

        using var ps = PowerShell.Create();
        ps.RunspacePool = _pool;  // borrow from pool rather than allocate a new runspace

        ps.AddScript("$ErrorActionPreference = 'Stop'");
        ps.AddScript(await File.ReadAllTextAsync(options.ScriptPath));
        ps.AddParameter("Request", context.Request);

        try
        {
            var results = await ps.InvokeAsync();
            await context.Response.WriteAsync(string.Join("\n", results.Select(r => r.ToString())));
        }
        catch (RuntimeException ex)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(ex.Message);
        }
        catch (ParseException)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("PowerShell script configuration error.");
        }
        // short-circuit: next() is deliberately not called — the script IS the response
    }

    public void Dispose() => _pool.Dispose();
}
```

Register as a singleton so the pool persists for the lifetime of the process:

```csharp
builder.Services.AddSingleton<IPipelinePlugin, PowerShellPlugin>();
```

**Sizing the pool:** `maxRunspaces` caps concurrent executions; requests arriving at an exhausted
pool block until a runspace returns. Set it to expected peak concurrency, not an arbitrary ceiling.
Pool exhaustion shows up in `conduitsharp.gateway.request.duration` as latency spiking while
concurrency stays flat.

### 2. Out-of-process execution

Memory-volatile scripts (large files, COM automation objects, heavy ERP extraction) make hosting the
PowerShell SDK inside the Kestrel process a liability: one leak or runaway allocation degrades the
gateway for every other route.

Run PowerShell as an external process via `System.Diagnostics.Process` instead. On exit the OS
reclaims 100% of the script's memory and the gateway process is untouched.

```csharp
public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
{
    var options = JsonSerializer.Deserialize<PsConfig>(config)!;

    var psi = new ProcessStartInfo("pwsh", $"-NonInteractive -File \"{options.ScriptPath}\"")
    {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
    };

    using var process = Process.Start(psi)!;
    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
    {
        context.ShortCircuit(500, stderr);
        return;
    }

    context.ShortCircuit(200, stdout);
}
```

**Trade-off:** `pwsh` startup adds 100-300ms cold-start per invocation. Fine for triggered ETL jobs
and on-demand reporting endpoints, not for low-latency APIs.

**At scale, pool the processes.** A new `pwsh` per request under concurrency exhausts resources on
its own. Keep a fixed pool of warm processes using the `RunspacePool` pattern, or a channel-based
worker queue serialising execution within a concurrency limit.

### 3. Script authoring: bypass PSCustomObject for heavy data

On large datasets the memory bloat usually comes from the cmdlets, not the engine. `Import-Csv`
hydrates every cell into a `PSCustomObject` with property bags, type metadata, and reflection
overhead; a 100k-row CSV can allocate several hundred MB before any business logic runs.

Scripts run on the .NET runtime and can call .NET classes directly. Prefer:

```powershell
# Instead of:
$rows = Import-Csv "data.csv"   # allocates PSCustomObject per row

# Use:
$reader = [System.IO.StreamReader]::new("data.csv")
while (-not $reader.EndOfStream) {
    $line = $reader.ReadLine()
    # process $line directly — no PSCustomObject allocated
}
$reader.Dispose()

# Or for JSON:
$json = [System.IO.File]::ReadAllText("data.json")
$parsed = [System.Text.Json.JsonSerializer]::Deserialize[MyType]($json)
```

This skips the PowerShell pipeline object model and runs at native .NET allocation rates. Same for
database queries: `System.Data.SqlClient` directly rather than `Invoke-SqlCmd` through the pipeline.

The gateway cannot enforce this. It is an authoring convention to document as a standard for
engineers writing production PowerShell plugins.

### Decision guide

| Scenario | Recommended approach |
|---|---|
| Low concurrency, simple scripts, <10ms script execution | `PowerShell.Create()` per request (README example), acceptable |
| Moderate concurrency, general-purpose scripts | `RunspacePool` with bounded max runspaces |
| Memory-volatile scripts, large file processing, COM automation | Out-of-process `pwsh` with process pool |
| Heavy ETL inside scripts | Out-of-process + native .NET classes in script (bypass `PSCustomObject`) |

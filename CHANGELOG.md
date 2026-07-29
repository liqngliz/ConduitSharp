# Changelog

All notable changes to this project will be documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [2.0.0] — unreleased

### Breaking — request-limit settings split into one budget per resource

`Gateway:RequestLimits` counted RAM and disk spill in a single combined total, which could not be
sized correctly for either: raising it to suit a large spill volume silently licensed the same
growth in RAM, so an overloaded gateway could be OOM-killed where it should have shed with a 503.
The combined total is replaced by two independent budgets.

| Removed | Replacement | Meters |
|---|---|---|
| `MaxTotalBufferedBodyBytes` (RAM + disk, 128 MiB) | `MaxDiskBufferedBodyBytes` (64 MiB) | spill-file bytes only — size against free disk |
| `MaxMemoryBufferedBodyBytes` (64 MiB) | `MaxRamBufferedBodyBytes` (64 MiB) | heap only — size against available memory |
| `MemoryBufferThresholdBytes` (1 MiB) | `RamBufferThresholdBytes` (1 MiB) | unchanged meaning; renamed for symmetry |

The removed keys are **rejected at startup** with a message naming the replacement, rather than
ignored — a config that still looks deliberate while silently running on defaults is the failure
mode this change exists to prevent.

Behaviour changes that follow:

- A buffered body is charged to exactly one budget at a time, moving from RAM to disk when it
  spills. Nothing is double-counted.
- The 503 load-shed now fires when **neither** budget has room, rather than when a combined total is
  gone. A body that fits entirely in RAM is served regardless of the disk budget.
- Body-capture prefixes are RAM-only (a tee holds pooled segments; a prefix read rents from
  `ArrayPool`; neither ever spills) and are now charged to `MaxRamBufferedBodyBytes`. Previously
  they were charged to the combined total, so a deployment with a large disk budget and a small
  memory tier could accumulate capture buffers in RAM up to the disk-inclusive number.
- `0` now reads the same way on both budgets: none of that resource is available. `0` RAM means
  every body spills; `0` disk means a body must fit RAM or be shed. Negative values are rejected at
  startup.
- **There is no longer an "unlimited" value, and `0` is its exact inverse.** On the old combined
  total, `0` disabled the check and meant "buffer without bound"; on either new budget it means
  "none of this resource". A config carried across unchanged does not merely lose a cap — it gains
  the opposite one. To keep unbounded behaviour, set the budget to a value large enough never to
  bind rather than to `0`. (The removed-key rejection catches the direct rename, but an operator
  who *re-types* `MaxDiskBufferedBodyBytes: 0` intending "unlimited" gets a gateway that refuses to
  spill; this is the one silent inversion in the change.)
- `RamBufferThresholdBytes` is floored at 4 KiB with no upper cap (previously clamped to 1 MiB).
  Raising it past 1 MiB trades large-object-heap allocation for skipping the disk round-trip.

### Breaking — body-capture plugins consolidated

`BodyCapturePlugin` is removed. `StreamingBodyCapturePlugin` (variant `body-capture`, formerly
`body-capture-streaming`) now covers both cases by choosing its path per request: it reuses the
gateway's existing seekable buffer when there is one (a retry route, or a body-reading plugin on the
same route), and otherwise tees a bounded prefix through `HttpLogging`. It keeps
`ReadsRequestBody => false`, so unlike the removed plugin it never forces whole-body buffering.

Existing routes using `"variant": "body-capture"` keep working unchanged. Routes using
`"variant": "body-capture-streaming"` must switch to `"body-capture"`.

The plugin's ceiling on a route's `maxSize` is configurable via `BodyCapture:MaxCaptureBytes`
(default 32 KiB), and a value beyond the addressable `int` range is rejected at startup.

### Breaking — header-transform config is nested by direction

`header-transform` config moved from a flat `{ add, set, remove }` to `{ request: {...}, response: {...} }`.
The flat shape is **rejected at startup** with a migration message. This closes a silent bug: the flat
config deserialized to an empty transform and did nothing, so a route that looked like it stripped
headers was a no-op. Migrate:

```json
// before
"config": { "set": { "X-Forwarded-By": "gw" }, "remove": ["X-Debug"] }
// after
"config": { "request": { "set": { "X-Forwarded-By": "gw" }, "remove": ["X-Debug"] } }
```

### Breaking — body-capture config is nested by direction

`body-capture` config moved from flat `{ "maxSize": N }` (request only) to
`{ "request": { "maxSize": N }, "response": { "maxSize": M } }`. The flat shape is **rejected at
startup** with a migration hint. A direction is captured only when its block is present with a
positive `maxSize`; omit it (or `maxSize: 0`) to skip it.

### Added

- **Response body capture.** `body-capture` now captures response bodies via a bounded write-through
  tee on `Response.Body`, so it runs on every route (including retry/buffered ones the request tee
  never sees) and captures binary responses too. Both prefixes are RAM-only; the gateway reserves
  `request.maxSize + response.maxSize` against `MaxRamBufferedBodyBytes` (the sum, since HttpLogging
  holds the request prefix until the response completes), so enabling response capture roughly
  doubles a route's capture reservation.
- **Response header transform.** `header-transform` now mutates response headers too, via a
  `response` block applied from `Response.OnStarting` just before the reply is sent. `remove` a leaky
  upstream header (`Server`, `X-Powered-By`) or `set` a security header (`X-Frame-Options`) on the way
  out. (`Server` note: Kestrel writes its own unless `AddServerHeader=false` on the host.)
- `retry.retryNonIdempotent` — opt in, per route, to retrying `POST`/`PATCH`. Off by default,
  because a non-idempotent request may already have been applied upstream, so a replay can
  double-apply it. Enabling it forces the route to buffer, since the retry loop needs a rewindable
  body.

### Fixed

- **The file body-capture sink's RAM is now budgeted.** `BodyCaptureToFilePlugin` rents a pooled
  `maxSize` buffer per request and holds it until its background writer drains the queue, but
  declared no `CaptureMemoryBytes` — so the gateway reserved nothing and the footprint multiplied by
  concurrency with nothing to shed it. It now declares the buffer, putting it under
  `MaxRamBufferedBodyBytes` and the 503. Its *disk* use stays deliberately unbudgeted: that is a
  persistent rolling log already bounded by `maxFileBytes`, not transient per-request spill, and
  reserving it per request would ratchet the disk budget to zero.

### Documentation

- **`AGENTDOC.md` and `ENTERPRISE.md` documented a plugin contract that does not exist.** Both showed
  `ExecuteAsync(PluginContext, PluginDelegate)` with `context.ShortCircuit(...)` and a
  `ResponseCaptureCallback`; the real signature is
  `ExecuteAsync(HttpContext, JsonElement, RequestDelegate)` and none of those types remain in `src/`.
  Following the docs produced a plugin that would not compile. Rewritten against the real interface,
  including the response-capture pattern `CachePlugin` actually uses (swap `Response.Body`, restore
  in a `finally`).

## [1.0.0] — 2026-07-25

First stable release — promotes `1.0.0-rc.1` to GA.

ConduitSharp is an internal integration gateway built on YARP: routes, clusters, retries, circuit
breakers and plugin chains are declared in `routes.json` and compiled into a separate ASP.NET
pipeline per route. Bodies stream by default — one is buffered only when something consumes the
buffer (a retry, or a body-reading plugin) — and buffering is bounded per request (413) and
gateway-wide (503 load-shed) so a burst of large uploads degrades instead of exhausting memory.

Upgrading from `1.0.0-rc.1` needs no changes. `IPipelinePlugin` gains one member,
`CaptureMemoryBytes(JsonElement)`, with a default implementation — existing plugins compile and
run unmodified.

Ships as a NuGet library (`ConduitSharp.Gateway.AspNetCore`), a `dotnet tool`, and a multi-arch
container image. Plugin packages for PowerShell and sliding-window rate limiting publish alongside.

### Added
- **Streaming-path capture memory is now budgeted** — `IPipelinePlugin` gains
  `CaptureMemoryBytes(JsonElement config)`, defaulting to `0`. A plugin that captures on the
  streaming path holds a bounded prefix per request, but that multiplies by concurrency and never
  touched `Gateway:RequestLimits:MaxTotalBufferedBodyBytes` — so nothing shed load as it grew, while
  a *buffering* plugin, whose bytes are budgeted, would already be returning 503. Declaring a
  non-zero value puts that memory under the same ceiling and the same 503. Plugins cannot reserve
  directly (the budget type is internal to the gateway assembly), hence a declared footprint read
  once at chain-compile time; it is config-aware because `maxSize` is per-route while plugins are
  singletons. **Source- and binary-compatible** for existing plugins via the default implementation.
- **Rotation for the file body-capture plugin** — `maxFileBytes` (default 128 MiB) rolls the sink
  keeping one backup, bounding it at ~2x. Appending forever hit the mount's size cap on tmpfs, or
  filled the volume and killed the writer on ENOSPC on real disk.
- **File body-capture example plugin** (`ConduitSharp.Plugin.BodyCaptureToFile`) — zero-allocation
  request-body capture to a JSONL sink via a bounded channel + pooled buffers (used by the s6
  logging benchmark; not published to NuGet).
- **PowerShell plugin cancellation + timeout** — a hung `.ps1` no longer blocks its thread; the
  script observes `RequestAborted` and a configurable `timeoutMs` (default 30s, `504` on timeout).

### Fixed
- **The file body-capture writer no longer dies silently.** It is the only writer, so a single IO
  error ended capture for the life of the process while every request still returned 200. It now
  takes an optional `ILogger` and reports the failure (optional so the plugin still constructs where
  nothing registers one).
- **ArrayPool leak in the file body-capture plugin** — `DropOldest` silently evicted queued
  entries without returning their pooled buffer under backpressure; switched to `DropWrite` with
  guaranteed buffer return.
- **Observability logging cost** — `IncludeScopes` disabled on the OpenTelemetry logging path
  (per-request serialization with no added signal); body-capture plugin log level is configurable.

### Performance
- **Body-capture reads its bounded prefix from a pooled buffer** — the `maxSize` path allocated a
  `char[maxSize]` plus a `StreamReader` per request to read a prefix whose size was already known.
  Truncation behaviour is unchanged.

### Benchmarks
Not shipped code, but the numbers published from it were wrong in ways worth recording:
- **s6 logging ingestion was capped by batch size, not by any gateway.** The OpenTelemetry Collector
  batched a whole flush interval of body-capture records into one ~15.9 MB push against Loki's 4 MiB
  gRPC receive limit; Loki answered 503 `ResourceExhausted` and the exporter retried the same
  oversized batch indefinitely. That one cause produced both published symptoms — ~3% ingestion and
  300–400% "ingestion" (the distributor counting retries). Records per push are now bounded and the
  receive ceiling raised; every arm measures 100%.
- **s6 posted 64 KB of random binary** and logged it as the log line. Loki's OTLP endpoint silently
  rejects invalid UTF-8, so OTLP arms "lost" records while file arms — writing bytes blindly —
  ingested everything. The payload is now a ~3 KB JSON body, which is what body capture actually
  sees.
- **Memory is reported with tmpfs counted.** The sampler polled every 2s while an arm lasted about
  one, leaving the column blank; it now reads the cgroup's own peak. tmpfs residency is reported
  alongside it, because tmpfs is RAM — a file-sink arm holds ~164 MiB the container's RSS never
  shows, which made the fastest arm look like the cheapest.
- **Ocelot now gets the gateway's `OTEL_BLRP_MAX_QUEUE_SIZE`.** Unset, it ran the SDK's 2048 default
  against the gateway's 20000 and dropped 16% of its records at 30k requests — which read as "Ocelot
  logs worse" when it was "Ocelot got a 10x smaller buffer".
- **New s6 arms**: the streaming tee (the plugin the docs describe, which s6 had never benchmarked),
  export deferred to the collector's `file_log` receiver, and that same sink on real disk rather
  than tmpfs.
- **Every s6 row now states the protocol it was measured under.** The published table named a
  concurrency the rows never ran at (the renderer took the first `c=` it found anywhere in the
  document, so s1's 96 was printed over s6's 50) and headed single runs with `QPS (med/?)`, implying
  a median. s6 labels now carry their concurrency, the renderer resolves it per scenario, and a
  single measured run is labelled as one. Same drift each time — the label, not the measurement.

What that campaign turned up, recorded because the old tables cannot be un-read: the ingestion
ceiling was the collector's batch size rather than any gateway; the payload's binary content was
being silently rejected by the sink; the memory column omitted tmpfs, which is RAM and where the
file arms hold ~164 MiB; and a competitor was running with a 10x smaller queue than ours. Each
error flattered ConduitSharp. The measurements were mostly sound — the labels were what needed
auditing.

## [1.0.0-rc.1] — 2026-07-19

### Fixed
- **Buffered routes now tell the server their body limit.** `SetMaxRequestBodySize` ran on the
  streaming path only, so a buffered route (retry or body-reading plugin) inherited Kestrel's
  30,000,000-byte transport default regardless of configuration: a route configured for 5 GB
  got 413 at ~28.6 MiB, and `maxRequestBodyBytes: 0` ("unlimited") was silently capped there
  while meaning genuinely unlimited on a streaming route. Both paths now set the limit
  identically. **Behaviour change:** a buffered route configured above ~28.6 MiB previously
  capped at Kestrel's default and now buffers what it was configured to — that is the fix, and
  it is also more memory/disk than such an operator was accidentally getting.

### Added
- **Instrumentation scope name + version in the file trace exporter.** Each JSON-line span now
  records `scopeName`/`scopeVersion` — the emitting `ActivitySource` and its version — matching the
  per-scope identity a real OTLP exporter records.
- **SlidingWindow limiter wired into the LegacyGateway example.** The drop-in DLL builds into the
  gateway's `plugins/` root, and a `/api/ratelimit-demo` route (3 req/30s per client) demonstrates
  the sliding-log 429 + `Retry-After` end to end.
- **Sliding-window rate limiting** (`examples/ConduitSharp.RateLimit.SlidingWindow`) — a drop-in
  `IRateLimiter` that refuses the burst a fixed window allows across its boundary (a caller can
  spend a full quota either side of an aligned seam, delivering 2x the nominal rate). Trades
  O(maxRequests) timestamps per active key for a limit that holds at every instant. Discovery is
  the existing plugins-directory seam; no routes.json change.

### Changed
- **Telemetry scope version tracks the package version.** The `ConduitSharp.Pipeline` and
  `ConduitSharp.Gateway` `ActivitySource`s (and the gateway `Meter`) previously hardcoded scope
  version `0.1.0`; they now read `AssemblyInformationalVersion` (generated from
  `Directory.Build.props` `<Version>`, SourceLink's `+<commit>` suffix stripped), so telemetry
  never drifts from the release.
- **`IRateLimiter` redesigned against two implementations.** It previously took the window and
  quota per *instance* (`bool TryAcquire(string key)`), which forced `RateLimitPlugin` to cache a
  limiter per distinct config and left `Retry-After` arithmetic stranded in the plugin — fixed-window
  arithmetic that no other algorithm could reuse. It now takes them per *call* and returns a
  `RateLimitDecision` carrying the retry hint:
  `RateLimitDecision TryAcquire(string key, int windowSeconds, long maxRequests)`.
  A limiter is a singleton like `IRateLimitStore`, so the plugin's per-config `ConcurrentDictionary`
  and `CreateLimiter` are gone, and each algorithm computes its own `Retry-After` — a fixed window
  counts to its boundary, a sliding log to its oldest request ageing out. Source-breaking for
  anyone who implemented the old interface; nothing in-repo did.

- **Two-tier body buffering — RAM, then disk, then 503.** Buffering used to degrade in one step:
  every body past `MemoryBufferThresholdBytes` (64 KiB) spilled to a temp file, and a single
  budget counted RAM and spill alike. The spill measures 2.8–4.7x slower than holding the body in
  memory. New `Gateway:RequestLimits:MaxMemoryBufferedBodyBytes` (default 64 MiB) carves a RAM
  tier out of `MaxTotalBufferedBodyBytes`: while it has headroom bodies buffer in memory; once
  full they spill from the first byte — slower, still served — and only when the combined budget
  is exhausted does the gateway shed with a 503. Buffered path, same machine: a 1 MB body drops
  870 → 262 µs (3.3x), a 10 MB body 6,490 → 5,409 µs (1.2x, since a body `Content-Length` proves
  will spill now skips the pointless RAM fill entirely).
- **`Gateway:RequestLimits:SpillDirectory`** — where the disk tier writes (default: system temp).
  Worth setting in containers: `/tmp` is frequently `tmpfs`, i.e. RAM, which silently turns the
  disk tier back into a memory tier and OOMs where the gateway would otherwise have degraded.
- **`./run.sh push-to-failure`** in the load rig — ramps 6 MB uploads per gateway under a shared
  container memory limit and records where each one stops coping (503 load-shed vs OOM-kill).

### Changed
- **`MemoryBufferThresholdBytes` default 64 KiB → 1 MiB**, clamp `[4 KiB, 512 KiB]` → `[4 KiB, 1 MiB]`.
  The threshold previously had to stay tiny because it was the only thing bounding aggregate RAM;
  the memory tier now does that, so per-request RAM can be generous. 1 MiB is a structural ceiling,
  not a preference: `FileBufferingReadStream` serves thresholds up to 1 MiB from `ArrayPool` and
  above it grows a bare `MemoryStream` by doubling, allocating ~2x the body on the LOH (measured
  cliff at exactly 1 MiB: 472 KB/req → 2.36 MB/req).
- **`MaxTotalBufferedBodyBytes` is now explicitly the RAM+spill outer bound**, with the memory tier
  carved out of it rather than added to it. Existing configs keep their meaning; RAM used by
  buffering is now explicitly bounded at 64 MiB where it was previously implicit in the 128 MiB
  combined budget.

### Changed
- **Bodies stream by default.** A request body is buffered only when something on the route
  consumes the buffer: a retry policy or a body-reading plugin. Routes with neither now behave
  as if `streamOnly` were set — no config change needed. Explicit `streamOnly: true` still
  works and is still validated at startup.
- **Method-aware buffering:** on a retry route, non-idempotent methods (`POST`, `PATCH`) stream —
  the retry loop can never replay them, so their buffer had no consumer.
- **Buffered bodies spill to disk.** The buffered path now uses `FileBufferingReadStream`:
  at most `Gateway:RequestLimits:MemoryBufferThresholdBytes` (new setting, default 64 KiB)
  lives on the heap; the rest goes to a temp file, nginx-style. A buffered 1 MB body drops
  from ~2 MB of heap allocation to ~51 KB; 10 MB drops from ~42 MB (with Gen2/LOH pressure)
  to the streaming baseline with zero Gen2 collections. `MaxTotalBufferedBodyBytes` now
  bounds memory + spill combined; the 413/503 limits are unchanged.

## [0.1.5] — 2026-07-16

A major refactor: the proxy engine is now YARP, and routes.json is built on YARP's own
config types. **Every routes.json needs migrating** — see the guide at the end of this entry.
Pre-1.0, so no compatibility shim is provided.

### Changed
- **Eager buffering by default:** ConduitSharp eagerly buffers all requests (subject to the `RequestBodyBudget`) so that plugins always receive a seekable stream. A new route-level `"streamOnly": true` option allows bypassing this for large uploads, streaming directly to YARP with zero allocations.
- **The forwarding engine is YARP's `IHttpForwarder`.** The hand-rolled `HttpClient` proxy, load
  balancers, and circuit breaker are gone. Per-route plugin chains are compiled once and run
  *inside* YARP's proxy pipeline, so the forward happens within the plugins' `next()`. HTTP/2,
  gRPC, WebSocket tunnelling, response streaming, and trailers now work on every route with no
  opt-in.
- **routes.json is built on YARP's `RouteConfig` and `ClusterConfig`,** used verbatim, with
  ConduitSharp's own concerns as typed siblings:
  `GatewayRoute { Id, Route, Cluster, Retry, CircuitBreaker, Plugins, Swagger, MaxRequestBodyBytes }`.
  Every YARP feature is now configurable with no schema work on our side — session affinity, active
  health checks, transforms, host matching, `SslProtocols`, per-destination `Host` — and header
  matching gains `Prefix`/`Contains`/`NotExists` modes the old dictionary shape could not express.
  `RouteId`, `ClusterId` and `Order` are filled in from the route's `id` and position, so they are
  never typed twice.
- **The plugin contract is now a route id, not a route object.** `Items["ConduitSharp.RouteId"]` is
  a `string`. Plugins only ever read `route.Id`, so the routes.json schema moved out of
  `ConduitSharp.Core` — which keeps its zero NuGet dependencies. A plugin author no longer inherits
  the gateway's config model, or YARP with it.
- **`X-Forwarded-For` / `-Proto` / `-Host` are now sent upstream** (YARP's default and correct
  gateway behaviour). The old proxy sent none. An upstream that infers the client IP will now see
  these headers.
- **`POST /admin/routes/reload` is a hot swap — no process restart.** It validates the incoming
  table against the same gates as startup, writes routes.json atomically, then swaps the route
  table, plugin chains, retry pipelines, and YARP config in place. Response body changed from
  `Routes updated. Gateway restarting.` to `Routes reloaded.`. Adding a *new plugin DLL* or an
  *mTLS client certificate* still needs a restart — both are resolved from DI at startup.
- Upstream forwarding no longer uses `IHttpClientFactory`. YARP builds one `HttpMessageInvoker`
  per cluster, and a custom `IForwarderHttpClientFactory` attaches per-route mTLS client
  certificates. A certificate that cannot be loaded now fails the gateway at startup rather than
  on the first request to that route.
- The `plugin.HttpProxy` trace span is replaced by `gateway.forward` (tagged with
  `conduitsharp.route_id` and `conduitsharp.attempt`).
- `RouteConfiguration` is renamed `GatewayRoute`. (Not `Route`: `Microsoft.AspNetCore.Routing.Route`
  already exists, and the `CS0104` ambiguity would land on every plugin author.)

### Added
- **Benchmark suite** (`benchmarks/`): BenchmarkDotNet microbenchmarks (route-table scaling,
  plugin dispatch, buffered-vs-streamOnly allocations, JWT hot path, head-to-head vs Ocelot)
  and an APISIX-method docker load rig (nginx 1 KB upstream, bombardier; scenarios: pure proxy,
  policy chain, 6 MB upload flood/load-shed, soak; head-to-head vs Ocelot and APISIX). CI
  (`benchmarks` workflow — manual or on release) publishes same-rig QPS *ratios* and allocation
  tables to the README with raw figures linked per run.
- **`retry` policy block**, driven by `Polly.Core`:
  `{ "maxAttempts": 3, "delayMs": 200, "backoff": "Exponential", "jitter": true, "retryOn": [502, 503, 504] }`.
  YARP ships no retry — a proxy cannot safely replay a half-streamed body — so the gateway buffers
  the request, rewinds it per attempt, and re-runs load balancing so a retry lands on a *different*
  node. Idempotent methods only.
- **`circuitBreaker` block**: `{ "threshold": 5, "cooldownMs": 10000 }`, implemented as a YARP
  `IPassiveHealthCheckPolicy`. YARP's stock passive policy is rate-over-a-window and cannot express
  a consecutive-failure threshold.
- **Load-balancing policies** beyond RoundRobin/Random: `PowerOfTwoChoices`, `LeastRequests`,
  `FirstAlphabetical`, plus any drop-in YARP `ILoadBalancingPolicy` DLL in `plugins/`. The name is
  validated at startup **and on reload** against the registered policy set, naming the offending
  route and listing what is available. A `LoadBalancingPolicy` enum gives C# callers the built-ins
  with compile-time safety.

### Fixed
- **Per-request observability was silently dead.** `IRequestObserver` implementations (the
  structured request log and the OTel request counter / duration / error metrics) were registered
  in DI but the middleware that notified them was deleted in the re-platforming. The outermost
  gateway middleware now fans out a `RequestObservation` in its `finally` on every path —
  forwarded, short-circuited, and unmatched (404) — and an observer that throws can never fail
  the request. Regression tests pin the wiring.
- **gRPC.** A cluster's `ForwarderRequestConfig` is static, but the right outbound protocol depends
  on how the client arrived: YARP's default silently downgrades HTTP/2 to HTTP/1.1 against a
  cleartext upstream. Inbound HTTP/2 now forwards with h2c prior knowledge.
- "Right path, wrong verb" returns **405** again, not 404.
- Cache coalescing always engages. Previously a route relying on implicit forwarding bypassed the
  response tee, so concurrent misses each hit the upstream.

### Removed
- `PluginKey`, `StructuredLogEntry`, and `RequestObservation.WasShortCircuited` — dead since the
  pipeline moved to native middleware; nothing read them.
- `HttpProxyPlugin`, `ConduitSharp.Traffic.LoadBalancing` (`ILoadBalancer`, `RoundRobinLoadBalancer`,
  `RandomLoadBalancer`, `NodeHealthTracker`), and `RouteConstraintMatcherPolicy` — superseded by
  YARP's forwarder, load-balancing policies, passive health checks, and native header/query matching.
- The `ConduitSharp.Plugin.YarpProxy` example — YARP is the engine now, not a drop-in.
- The `ConduitSharp.Plugin.FirstMatchRouting` example — declaration order maps to `RouteConfig.Order`,
  so first-match-wins is native. Custom `MatcherPolicy` DLLs are still discovered from `plugins/`.
- `HttpVerb`, `RouteMatchConfig`, `UpstreamConfig`, `UpstreamNode`, `UpstreamTlsOptions`,
  `LoadBalancingStrategy` — all superseded by YARP's records.
- `nodes[].weight`. It appeared in every example but `UpstreamNode` had no `Weight` property, so it
  was silently dropped by the deserializer and mapped to nothing. Weighted balancing would be a
  natural drop-in `ILoadBalancingPolicy`.
- The `retryCount` / `retryDelayMs` shorthand — it hung off `upstream`, which no longer exists.

### Migrating routes.json

```jsonc
// 0.1.4                                    // 0.1.5
{                                           {
  "id": "orders",                             "id": "orders",
  "match": {                                  "route": {
    "path": "/api/orders/{**rest}",             "match": {
    "methods": ["GET", "POST"],                   "path": "/api/orders/{**rest}",
    "headers": { "X-Internal": "yes" },           "methods": ["GET", "POST"],
    "queryParams": { "v": "2" }                   "headers": [ { "name": "X-Internal",
  },                                                             "values": ["yes"],
                                                                 "mode": "ExactHeader" } ],
                                                  "queryParameters": [ { "name": "v",
                                                                         "values": ["2"],
                                                                         "mode": "Exact" } ]
                                                }
                                              },
  "upstream": {                               "cluster": {
    "loadBalancingStrategy": "RoundRobin",      "loadBalancingPolicy": "RoundRobin",
    "nodes": [                                  "destinations": {
      { "host": "http://o1:8081" },               "node-0": { "address": "http://o1:8081" },
      { "host": "http://o2:8081" }                "node-1": { "address": "http://o2:8081" }
    ],                                          },
    "timeoutMs": 5000,                          "httpRequest": { "activityTimeout": "00:00:05" },
    "tls": {                                    "httpClient": {
      "skipCertificateVerification": true         "dangerousAcceptAnyServerCertificate": true
    },                                          }
    "retryCount": 2,                          },
    "retryDelayMs": 200,                      "retry": { "maxAttempts": 3, "delayMs": 200 },
    "circuitBreakerThreshold": 5,             "circuitBreaker": { "threshold": 5,
    "circuitBreakerCooldownMs": 10000                              "cooldownMs": 10000 },
  },
  "plugins": [ ... ]                          "plugins": [ ... ]
}                                           }
```

- `"upstream": null` (plugin-only routes) becomes `"cluster": null`.
- `retryCount: N` becomes `retry: { maxAttempts: N + 1 }` — it counts *attempts*, not retries.
- Destination keys are yours to choose; they show up in logs, metrics and traces.
- Everything can still be written in camelCase; YARP's records bind case-insensitively.

### Plugin authors

`context.Items["ConduitSharp.Route"]` is now `context.Items["ConduitSharp.RouteId"]`, and it holds a
`string`:

```csharp
- var route   = (RouteConfiguration)context.Items["ConduitSharp.Route"]!;
- var routeId = route.Id;
+ var routeId = (string)context.Items["ConduitSharp.RouteId"]!;
```

Recompile against `ConduitSharp.Core` 0.1.5. Nothing else in the plugin contract changed.


## [0.1.4] — 2026-07-13

### Fixed
- External plugin binaries broke when `PluginName` enum members were reordered or removed —
  removing `PowerShell` shifted `HttpProxy`'s numeric value, so a compiled drop-in plugin
  built against the old enum layout silently registered under the wrong identity. Plugin
  identity is now a stable `string Id` declared by each plugin (`IPipelinePlugin.Id`), and
  the registry (`PluginKey`) is keyed by `(string Id, string? Variant)` instead of
  `(PluginName, Variant)`. Route configs still declare plugins by `PluginName` in JSON;
  `PluginPipelineExecutor` maps that enum to the registered plugin's `Id` internally, so
  existing route files are unaffected. **Breaking change** for any custom `IPipelinePlugin`
  implementation — add a stable `Id` property (e.g. `"http-proxy"`, `"cache"`) alongside the
  existing `Name`/`Variant`, and recompile against the updated `ConduitSharp.Core`.
- The example plugin packages (`ConduitSharp.Cache.RedisProtocol`,
  `ConduitSharp.RateLimit.RedisProtocol`, `ConduitSharp.Plugin.YarpProxy`,
  `ConduitSharp.Plugin.SpecificityRouting`) published as 0.1.3 depended on `[0.1.2, )` of
  `ConduitSharp.Core`/`Traffic`/`Gateway.AspNetCore` — the `PackageReference` version bump
  needed for the `Id` property landed in a commit made after the 0.1.3 tag. Republished as
  0.1.4 with correct same-version dependency ranges.

### Changed
- Cached responses were captured and stored as UTF-8 strings, corrupting binary or
  content-encoded (e.g. `Content-Encoding: gzip`) 2xx responses on cache replay. Response
  capture and cache storage now use raw bytes end-to-end: `CachedResponse.Body` is `byte[]`,
  `PluginContext.ResponseCaptureCallback` delivers `byte[]`, and a new
  `PluginContext.ShortCircuit(int, byte[])` overload lets `CachePlugin` replay cached bodies
  byte-for-byte instead of round-tripping through text. **Breaking change** for any custom
  `ICacheService` or plugin that reads `ResponseCaptureCallback`/`CachedResponse.Body` as a
  `string` — recompile against the updated `ConduitSharp.Core`/`ConduitSharp.Traffic`.
- Architecture and README diagrams now surface all pluggable seams: the `IRouteMatcher`
  routing strategy (specificity drop-in), the `IRateLimitStore` distributed rate-limit
  drop-in, and the aggregated Swagger UI add-on — previously only the cache drop-in was shown
- All `ConduitSharp.*` package references in `examples/` bumped from 0.1.0 to 0.1.2, then to
  0.1.4

## [0.1.2] — 2026-07-10

### Changed
- Version alignment release — `<Version>` in `Directory.Build.props` and the git tag now
  match; all `ConduitSharp.*` packages published to nuget.org as 0.1.2

## [0.1.1] — 2026-07-10

### Added
- End-to-end test proving the pluggable routing strategy: the
  `ConduitSharp.Plugin.SpecificityRouting` DLL dropped into a real gateway process's
  plugins root replaces the built-in first-match-wins matcher
- Focused per-package NuGet READMEs

### Changed
- Example projects consume the published NuGet packages instead of project references

### Security
- `System.Security.Cryptography.Xml` bumped 9.0.5 → 9.0.15 (Dependabot)

## [0.1.0] — 2026-07-09

Initial public release.

### Added
- Core plugin pipeline: `IPipelinePlugin`, `PluginContext`, `PluginDelegate`, `PluginPipelineExecutor`
- Route matching: path templates, method filtering, header and query parameter matching, first-match-wins
- Pluggable routing strategy via `IRouteMatcher` — replace the built-in first-match-wins matcher
  through DI or a plugins-root DLL; `examples/ConduitSharp.Plugin.SpecificityRouting` ships
  ASP.NET Core-style most-specific-wins matching as a drop-in
- Built-in plugins: `jwt-auth`, `api-key-auth`, `rate-limit`, `cache`, `header-transform`, `http-proxy`
- Plugin extension without forking Core: `PluginName.Custom` + a self-chosen `variant` string
  registers genuinely new plugin types; built-in names stay a closed, startup-validated enum
- `jwks-jwt-auth` plugin: JWKS key fetch with caching, RS256/384/512 and ES256/384/512 support
- Claim-based authorization (RBAC) for `jwt-auth` and `jwks-jwt-auth` via a `requiredClaims` config block (`equals`/`anyOf`/`allOf`/existence-only matchers, dotted-path claim lookup for nested claims like Keycloak's `realm_access.roles`, delimiter-splitting for space-delimited scopes) — a valid token lacking the required claim gets 403, not 401
- Hashed API key plugin (`api-key-auth-hashed`) — keys stored as SHA-256 hashes, never in plaintext
- Embeddable gateway library (`ConduitSharp.Gateway.AspNetCore`) — `AddConduitSharpGateway()` /
  `UseConduitSharpGateway()` host any ASP.NET Core app; the standalone Host, dotnet tool, and
  Docker image are thin shells over it
- Aggregated Swagger UI as an opt-in add-on package (`ConduitSharp.Gateway.AspNetCore.Swagger`) —
  fetches or serves per-route OpenAPI specs at `/swagger`, with SSRF and path-traversal guards
- Distributed response cache drop-in (`examples/ConduitSharp.Cache.RedisProtocol`) — shared
  Valkey/Redis cache across instances with request coalescing; fails open
- Distributed rate-limit store drop-in (`examples/ConduitSharp.RateLimit.RedisProtocol`) — one
  shared limit across instances via the `IRateLimitStore` seam; fails open
- Round-robin and random load balancing across multiple upstream nodes
- Per-node circuit breaker (`circuitBreakerThreshold` / `circuitBreakerCooldownMs`) and
  idempotent-method retries with per-attempt timeout (504 on expiry, 502 on connection failure)
- Request body limits: per-route/global `maxRequestBodyBytes` (413) and an aggregate
  in-flight body budget (503)
- mTLS client certificates per route (PFX file or Windows certificate store)
- Self-signed certificate bypass per upstream (`skipCertificateVerification`)
- Drop-in external plugin loading from `plugins/` directory at startup
- Health endpoints: `/healthz` (liveness) and `/readyz` (readiness — route table loaded,
  deliberately independent of upstream reachability)
- Admin API: `POST /admin/routes/reload` (atomic file swap, audit trail) and
  `DELETE /admin/cache/{routeId}`, gated by SHA-256 hashed key authentication
- OpenTelemetry traces and metrics via `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter`
- Per-plugin child spans via `PipelineTelemetry.ActivitySource` — traces show the full plugin execution waterfall
- File-based OTel span exporter (`FileSpanExporter`) — writes JSONL traces locally without a collector
- `routes.schema.json` — JSON Schema for IDE autocomplete and inline validation of routes.json
- `GATEWAY_CONFIG_FILE` environment variable for environment-specific observability overlays (`configuration-vm/`, `configuration-docker/`)
- YARP proxy example plugin (`examples/ConduitSharp.Plugin.YarpProxy`) — HTTP/2, gRPC, and WebSocket forwarding
- PowerShell plugin (`examples/ConduitSharp.Plugin.PowerShell`) — executes `.ps1` scripts as gateway handlers
- LegacyGateway example: multi-route end-to-end demo with two upstream services, a gRPC route, and a PowerShell ERP report
- Docker Compose stack for LegacyGateway example with Aspire Dashboard for OTLP visualisation
- `make docker-up` / `pwsh start.ps1 -DockerUp` commands for the containerised example
- Test suite at every layer: unit, integration (routing, pipeline, load balancing, auth, traffic,
  tracing, admin API, upstream errors), out-of-process E2E, Docker mTLS E2E, Grafana pipeline E2E
- Startup validation — missing routes file, malformed JSON, duplicate IDs, unknown plugin name all fail fast with a clear error
- Apache 2.0 license

### Changed
- Gateway port in LegacyGateway example changed from 5000 to 5050 (avoids conflict with macOS AirPlay Receiver)
- OTLP export disabled by default (`OtlpOptions.Enabled = false`) — must be opted in explicitly
- `ExportProcessorType.Simple` on both trace and metric OTLP exporters — eliminates batch-buffer delay in reported durations
- HttpClient instrumentation filter now derives the excluded host from the configured OTLP endpoint rather than hard-coding `"aspire-dashboard"`
- `FileExporterOptions.TracesPath` now resolved relative to `Gateway:BasePath` instead of the dotnet process working directory
- `make stop` / `pwsh start.ps1 -Stop` use three-layer kill: PID file → port scan → process name

### Fixed
- `FileSpanExporter.Export` now returns `ExportResult.Failure` on write errors instead of propagating exceptions
- `Retry-After` header was already present on 429 responses (audit finding was incorrect)

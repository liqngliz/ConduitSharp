<p align="center">
  <img src="assets/branding/logo.svg" alt="ConduitSharp logo" width="112" />
  <h1 align="center">ConduitSharp</h1>
  <p align="center">The .NET API Gateway — a JSON-driven internal integration gateway on YARP, with native retries, circuit breakers, and a memory-bounded per-route plugin pipeline.</p>
  <p align="center">
    <a href="https://github.com/liqngliz/ConduitSharp/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/liqngliz/ConduitSharp/actions/workflows/ci.yml/badge.svg?branch=main" /></a>
    <a href="https://www.nuget.org/packages/ConduitSharp.Gateway.AspNetCore"><img alt="NuGet" src="https://img.shields.io/nuget/v/ConduitSharp.Gateway.AspNetCore?logo=nuget" /></a>
    <img alt="License: Apache 2.0" src="https://img.shields.io/badge/license-Apache%202.0-blue" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet" />
    <img alt="Language: C#" src="https://img.shields.io/badge/language-C%23-239120?logo=csharp" />
  </p>
</p>

---

ConduitSharp **Internal Integration Gateway** built on top of Microsoft's YARP (Yet Another Reverse Proxy). It is specifically designed to work in tandem with edge gateway services (like Azure APIM, Cloudflare, or Kong), sitting behind the network perimeter to focus on internal traffic orchestration, legacy workload integration, and microservice routing.

It extends YARP with features like native retries, circuit breakers, a strict memory-bounded plugin pipeline, and robust API configuration hot-reloading. It supports both codeless deployments based purely on JSON configuration and embedded deployments with dependency injection.

## 🎯 Why ConduitSharp

**Powered by YARP:** Built on top of Microsoft’s battle-tested YARP engine rather than reinventing socket-forwarding from scratch. You get YARP's world-class load balancing, connection pooling, and performance, wrapped in a developer-friendly API gateway experience.

**Native Retries & Circuit Breakers:** YARP deliberately omits retries and circuit breaking out of the box because doing them safely requires body buffering. ConduitSharp fills this gap, offering robust JSON-configured resilience policies built with strict safety rails:
  - **Streaming by default:** a body is buffered only when something consumes the buffer — a retry policy (idempotent methods only; a POST on a retry route still streams) or a body-reading plugin. Every other request is pure forward-only streaming, automatically.
  - **Off-heap buffering:** when a body *is* buffered, only the first 64 KiB lives on the heap; the rest spills to a temp file (nginx-style) — large uploads never touch the Large Object Heap.
  - **Memory Budgets:** buffering is additionally capped per request (413) and gateway-wide (503 load-shed) to prevent memory exhaustion.
  - **Fail-Fast Validation:** accidentally mix an explicit `"streamOnly": true` route with a payload-reading plugin? The gateway catches the conflict at startup, preventing mid-flight runtime errors.

**First-Class OpenTelemetry:** Built for production observability from day one. Native OpenTelemetry metrics and distributed tracing are baked directly into the proxy pipeline and are easily configured via environment variables.

---

## ⚡ What You Get — Every Deployment

Whether you embed it in a custom application or run it as a standalone appliance:

**Zero/Low-Code Configuration:** Define routes, upstream clusters, load balancing rules, and plugin chains entirely via the `routes.json` file.


**Hot-Reloading without Dropping Requests:** Update routing configurations via the `POST /admin/routes/reload` API. The gateway atomically swaps the routing table in-memory using YARP's `InMemoryConfigProvider`. In-flight requests gracefully finish on their original rules.


**Batteries Included (Shipped Plugins):** Comes out of the box with highly-optimized built-in plugins for common gateway needs:
  - JWT & API Key Authentication
  - Distributed Rate Limiting
  - Response Caching
  - Header Transformation

---

## 📊 Benchmarks

<!-- BENCH:START -->
### Throughput — relative, same rig, sequential runs

#### 125 connections

```mermaid
xychart-beta
    title "Relative QPS at 125 connections — higher is faster"
    x-axis ["ConduitSharp", "APISIX", "Ocelot", "no gateway (direct)"]
    y-axis "QPS vs ConduitSharp = 1.00" 0 --> 4.71
    bar [1.00, 1.02, 0.68, 4.10]
```

| gateway | QPS (relative) | p50 | p99 |
|---|---:|---:|---:|
| ConduitSharp | 1.00× | 4.32 ms | 18.38 ms |
| APISIX | 1.02× | 2.73 ms | 23.70 ms |
| Ocelot | 0.68× | 6.76 ms | 23.02 ms |
| *(no gateway — direct to nginx)* | 4.10× | 1.26 ms | 3.43 ms |

#### 512 connections

```mermaid
xychart-beta
    title "Relative QPS at 512 connections — higher is faster"
    x-axis ["ConduitSharp", "APISIX", "Ocelot"]
    y-axis "QPS vs ConduitSharp = 1.00" 0 --> 1.16
    bar [1.00, 1.01, 0.70]
```

| gateway | QPS (relative) | p50 | p99 |
|---|---:|---:|---:|
| ConduitSharp | 1.00× | 18.08 ms | 64.43 ms |
| APISIX | 1.01× | 18.09 ms | 64.09 ms |
| Ocelot | 0.70× | 27.39 ms | 80.31 ms |

Pure proxy, 1 KB upstream response, bombardier, gateways benched sequentially on the
identical rig. **Measured on shared GitHub Actions runners (4 vCPU) — only ratios are
meaningful there; absolute QPS on shared CI is noise.** Raw figures for this exact run:
[CI run](https://github.com/liqngliz/ConduitSharp/actions/runs/29905486090). Method & how to reproduce on pinned hardware:
[benchmarks/load](benchmarks/load/README.md).
<!-- BENCH:END -->

<!-- BENCH-MICRO:START -->
### Head-to-head microbenchmarks — ConduitSharp vs Ocelot (.NET gateways only)

#### Route-table scaling — request hits the last of N routes

```mermaid
xychart-beta
    title "Allocated per request, N routes configured — lower is better"
    x-axis ["ConduitSharp N=1", "ConduitSharp N=500", "Ocelot N=1", "Ocelot N=500"]
    y-axis "KB / request" 0 --> 182.01
    bar [14.40, 14.41, 25.96, 158.27]
```

| Method     | Gateway      | RouteCount | Mean     | Error    | StdDev   | Gen0    | Allocated |
|----------- |------------- |----------- |---------:|---------:|---------:|--------:|----------:|
| **ProxiedGet** | **ConduitSharp** | **1**          | **231.4 μs** | **603.5 μs** | **33.08 μs** |  **1.4648** |   **14.4 KB** |
| **ProxiedGet** | **ConduitSharp** | **500**        | **242.2 μs** | **308.6 μs** | **16.92 μs** |  **1.4648** |  **14.41 KB** |
| **ProxiedGet** | **Ocelot**       | **1**          | **374.6 μs** | **592.0 μs** | **32.45 μs** |  **1.9531** |  **25.96 KB** |
| **ProxiedGet** | **Ocelot**       | **500**        | **469.1 μs** | **193.7 μs** | **10.62 μs** | **16.6016** | **158.27 KB** |

ConduitSharp rides ASP.NET endpoint routing's DFA: flat time and allocations at any
route count. Ocelot's route finder scans templates per request — cost grows with N.

#### Policy chain — JWT auth (HS256) + rate limit on both sides

```mermaid
xychart-beta
    title "Allocated per request — JWT + rate limit, lower is better"
    x-axis ["ConduitSharp", "Ocelot"]
    y-axis "KB / request" 0 --> 43.17
    bar [20.92, 37.54]
```

| Method    | Gateway      | Mean     | Error    | StdDev   | Gen0   | Allocated |
|---------- |------------- |---------:|---------:|---------:|-------:|----------:|
| **AuthedGet** | **ConduitSharp** | **452.5 μs** | **419.6 μs** | **23.00 μs** | **1.9531** |  **20.92 KB** |
| **AuthedGet** | **Ocelot**       | **489.4 μs** | **828.0 μs** | **45.38 μs** | **2.9297** |  **37.54 KB** |

#### Upload bodies — POST (streamed) and PUT on a retry route (buffered)

```mermaid
xychart-beta
    title "Allocated per request, 10 MB body — lower is better"
    x-axis ["ConduitSharp", "ConduitSharp-retry", "Ocelot", "Ocelot-retry"]
    y-axis "KB / request" 0 --> 23357.45
    bar [10043.69, 10118.78, 10056.69, 20310.83]
```

| Method   | Gateway            | BodyKB | Mean        | Error       | StdDev    | Gen0     | Gen1     | Gen2     | Allocated   |
|--------- |------------------- |------- |------------:|------------:|----------:|---------:|---------:|---------:|------------:|
| **PostBody** | **ConduitSharp**       | **1**      |    **248.6 μs** |    **349.8 μs** |  **19.18 μs** |   **1.4648** |        **-** |        **-** |    **16.01 KB** |
| **PostBody** | **ConduitSharp**       | **10240**  | **15,933.5 μs** | **13,202.0 μs** | **723.65 μs** | **343.7500** | **312.5000** |        **-** | **10043.69 KB** |
| **PostBody** | **ConduitSharp-retry** | **1**      |    **407.8 μs** |    **372.7 μs** |  **20.43 μs** |   **0.9766** |        **-** |        **-** |    **17.66 KB** |
| **PostBody** | **ConduitSharp-retry** | **10240**  | **23,346.3 μs** | **12,523.8 μs** | **686.47 μs** | **312.5000** | **187.5000** |        **-** | **10118.78 KB** |
| **PostBody** | **Ocelot**             | **1**      |    **420.7 μs** |    **374.3 μs** |  **20.52 μs** |   **1.9531** |        **-** |        **-** |    **29.19 KB** |
| **PostBody** | **Ocelot**             | **10240**  | **16,785.0 μs** | **11,937.9 μs** | **654.36 μs** | **375.0000** | **343.7500** |        **-** | **10056.69 KB** |
| **PostBody** | **Ocelot-retry**       | **1**      |    **490.8 μs** |    **674.5 μs** |  **36.97 μs** |   **3.9063** |        **-** |        **-** |    **41.44 KB** |
| **PostBody** | **Ocelot-retry**       | **10240**  | **19,207.3 μs** | **14,671.1 μs** | **804.17 μs** | **625.0000** | **593.7500** | **312.5000** | **20310.83 KB** |

Both gateways stream a POST upload — retries never apply to a POST, whose body could not
be safely replayed, so neither side allocates a buffer. Identical work: the delta is
per-request overhead, and ConduitSharp allocates about half.

The `-retry` arms are the buffered path, same-on-same: a PUT each side must be able to
replay. Ocelot ships no retry, so it runs the load rig's, built on its official Polly
package's `AddPolly` seam
([BufferingPollyHandler](benchmarks/load/ocelot/BufferingPollyHandler.cs)) — the whole body
held on the heap via `LoadIntoBufferAsync`, per in-flight request, no ceiling. ConduitSharp
buffers up to 1 MiB in pooled memory and spills the rest to tmpfs on this rig — bounded
RAM either way, and the tiers degrade to disk and then 503 instead of OOM under
concurrency (measured in [benchmarks/load](benchmarks/load/README.md)).

Both gateways in-proc (TestServer), forwarding over a real loopback socket to the same
1 KB upstream — identical downstream cost, the delta is gateway overhead.
**Allocated per request is deterministic — compare that column;** time columns are
trend-only on shared CI runners. APISIX is nginx/Lua and cannot be micro-benched
in-process; its comparison is the throughput ratio table above. Full tables:
[docs/benchmarks/micro.md](docs/benchmarks/micro.md) · [source run](https://github.com/liqngliz/ConduitSharp/actions/runs/29898589358)
<!-- BENCH-MICRO:END -->

<!-- BENCH-MATRIX-SUMMARY:START -->
### Body handling under load — the s1..s5 matrix (relative QPS, same rig)

#### Streaming path — a 1 MB body nobody needs to replay

```mermaid
xychart-beta
    title "s1 — out of the box, retries set, 1 MB POST: relative QPS (higher is faster)"
    x-axis ["ConduitSharp", "Ocelot", "APISIX"]
    y-axis "QPS vs ConduitSharp = 1.00" 0 --> 1.15
    bar [1.00, 0.87, 0.59]
```

| scenario (c=96) | ConduitSharp | Ocelot | APISIX |
|---|---:|---:|---:|
| s1 — retries configured, 1 MB POST (ConduitSharp streams it: method-aware) | 1.00× | 0.87× | 0.59× (1.8x to disk) |
| s2 — pure streaming, 1 MB POST (APISIX de-tuned to qualify, forfeiting retry) | 1.00× | 0.99× | 0.73× |

#### Buffered path — forced to disk (s4), and tmpfs as the answer (s5)

```mermaid
xychart-beta
    title "1 MB PUT, disk vs tmpfs spill: relative QPS (higher is faster)"
    x-axis ["ConduitSharp — disk", "APISIX — disk", "APISIX — tmpfs", "ConduitSharp — tmpfs"]
    y-axis "QPS vs ConduitSharp disk = 1.00" 0 --> 5.81
    bar [1.00, 3.33, 3.31, 5.05]
```

| scenario (c=96) | ConduitSharp | Ocelot | APISIX |
|---|---:|---:|---:|
| s4 — buffering forced onto disk, 1 MB PUT | 1.00× (1.0x to disk) | — | 3.33× (1.8x to disk) |
| s5 — buffered, spill target is tmpfs, 1 MB PUT | 1.00× | — | 0.66× (1.8x to disk) |

Structured comparison: each scenario fixes the shape of the work, then compares gateways doing that shape, with bytes-written-to-storage measured rather than assumed. s4 is the honest row: forced entirely onto disk, nginx wins — the design's answer is s5 and the RAM tier that makes disk rare. Full tables, method, and the parts that hurt: [benchmarks/load](benchmarks/load/README.md#structured-comparison--measured-not-hand-typed) · [CI run](https://github.com/liqngliz/ConduitSharp/actions/runs/29659383588).
<!-- BENCH-MATRIX-SUMMARY:END -->

Full microbenchmark tables (routing, plugin dispatch, buffered-vs-stream allocations, JWT
hot path) are published to [docs/benchmarks/micro.md](docs/benchmarks/micro.md); source in
[benchmarks/ConduitSharp.Benchmarks](benchmarks/ConduitSharp.Benchmarks), load rig and
method in [benchmarks/load](benchmarks/load/README.md).

---

## 🚀 Quick Start

Run the bundled LegacyGateway example — a gateway with six routes, three upstream services (REST, gRPC, and a streamOnly upload), a PowerShell plugin, JWT + API-key auth, rate limiting, caching, and OpenTelemetry traces — all wired from `routes.json` with no code changes.

```bash
cd examples/LegacyGateway
make docker-up            # macOS / Linux — Docker Compose + Aspire Dashboard
pwsh start.ps1 -DockerUp  # Windows — same stack
```

```bash
# Health check — no auth
curl http://localhost:5050/health

# Inventory — API key
curl http://localhost:5050/api/inventory \
     -H "X-Api-Key: demo-api-key-conduitsharp-example"

# Finance report — JWT (token printed by make docker-up)
curl http://localhost:5050/finance/reports/margin \
     -H "Authorization: Bearer $TOKEN"
```

Open the **Aspire Dashboard** at http://localhost:18888 to see every request's trace. Prefer Grafana/Tempo/Prometheus/Loki? Run `make docker-grafana` and open http://localhost:3000. No Docker? `make run` (macOS/Linux) or `pwsh start.ps1` (Windows) runs the same example as local processes with file-based traces.

---

## 🏗️ Architectural Choices & Trade-offs

ConduitSharp makes deliberate architectural decisions to solve real-world gateway deployment problems. While some choices may seem unconventional at first glance, they are specifically engineered for flexibility and safety.

### 1. Dynamic DLL Loading (The "Appliance" Model)

**The Choice:** Allowing `.dll` files to be dropped into a `plugins/` directory for runtime loading.
**Why:** This is an explicit *option* designed for the Strangler Fig pattern or legacy non-Kubernetes environments where you cannot easily deploy immutable containers. If your team uses modern immutable infrastructure (like Kubernetes), you simply set `options.EnablePluginDirectoryScan = false` and use standard ASP.NET Core Dependency Injection instead.

### 2. Buffering Request Bodies

**The Choice:** Buffer a request body *only when something consumes the buffer* — a retry policy or a body-inspecting plugin. Everything else streams, automatically.
**Why:** **Upstream Retries** need a rewindable body, and plugins like HMAC verification or audit logging must inspect the payload. Those are the only consumers, so they are the only cases that pay:
- **Method-aware:** retries apply to idempotent methods only (`GET`, `PUT`, …) — so a `POST` upload through a retry route still streams; its body could never be replayed anyway.
- **Tiered, not a cliff:** a buffered body is held in RAM while the memory tier (`MaxMemoryBufferedBodyBytes`, default 64 MiB) has headroom — measured 3.3x faster than spilling for a 1 MB body. Once it is full, further bodies spill to a temp file, nginx-style (`proxy_request_buffering`), and are still served. Only when `MaxTotalBufferedBodyBytes` (RAM + spill) is gone does the gateway shed with a 503. Availability degrades in steps rather than falling over.
- **Off-heap:** per-body RAM is capped at `MemoryBufferThresholdBytes` (default 1 MiB, clamped there because `FileBufferingReadStream` stops pooling above it). Large uploads never churn the .NET Large Object Heap.
- **Safety:** buffering is enforced by a per-request limit (413) and the gateway-wide `RequestBodyBudget` (503 load-shed) to prevent memory exhaustion.
- **Escape hatch:** `"streamOnly": true` still forces pure streaming and is validated at startup against retry policies and body-reading plugins.

### 3. Stateful Hot-Reloading API

**The Choice:** A `POST /admin/routes/reload` API that writes `routes.json` to local disk.
**Why:** This provides zero-downtime updates for standalone VM/AppService deployments. However, it is **inert by default**. To prevent split-brain scenarios in Kubernetes clusters, you simply do not provide an `AdminKeyHash` in your configuration. This disables the API, allowing you to manage `routes.json` via a standard Kubernetes ConfigMap rollout.

### 4. Custom JSON Plugin Pipeline vs. Standard Middleware

**The Choice:** A custom `IPipelinePlugin` interface driven by JSON arrays instead of `app.Use()` middleware.
**Why:** YARP natively runs a single, monolithic pipeline for all proxied routes. ConduitSharp solves this by dynamically compiling an isolated `IApplicationBuilder` pipeline *for every single route* defined in the JSON. This allows Route A to run Heavy Rate Limiting and JWT Auth, while Route B runs pure streaming with zero middleware overhead, all built using native Kestrel middleware primitives under the hood.

---

## 🚀 Two Paths for Hosting & Extension - Example Project

ConduitSharp is designed to be flexible, supporting two distinct deployment models based on how you want to extend it. The provided example projects demonstrate these models side-by-side.

```mermaid
flowchart LR
    subgraph deploy ["Deployed as a standalone binary or embedded"]
        client(["Client"]) -->|HTTP| gw["ConduitSharp Gateway"]
        gw --> inv["InventoryService x2\napi-key-auth + rate-limit"]
        gw --> ord["OrderService\njwt-auth"]
        gw --> grt["GreeterService (gRPC, h2c)\nhttp-proxy — HTTP/2 verbatim"]
        gw --> upl["OrderService (upload)\nstreamOnly — zero-alloc passthrough"]
        gw --> fin["jwt-auth + rate-limit + cache + power-shell\nGet-MarginReport.ps1"]
    end

    gw -.OTLP.-> choice{"observability stack"}

    subgraph docker ["Available only when running the gateway in Docker for example"]
        subgraph opt1 ["Option 1 — make docker-up"]
            choice -.-> aspire["Aspire Dashboard\n:18888"]
        end

        subgraph opt2 ["Option 2 — make docker-grafana"]
            choice -.-> collector["OTel Collector"]
            collector --> tempo["Tempo (traces)"]
            collector --> loki["Loki (logs)"]
            collector --> prom["Prometheus (metrics)"]
            tempo & loki & prom --> grafana["Grafana\n:3000"]
        end
    end
```

### Path 1: The Modern "Code-First" Gateway (Recommended)

This approach treats ConduitSharp like a standard .NET library. It is ideal for mature engineering teams who prefer immutable infrastructure, infrastructure-as-code, and standard ASP.NET Core practices.

**Why choose this path?**

- **Standard Dependency Injection:**: Use (`builder.Services.AddSingleton<...>`) to register your custom plugins.
- **Custom Plugins:** Written in C# alongside your gateway setup without managing separate `.dll` deployments. Plugins are available through nuget
- **Standard ASP.NET**: Mix and match with standard ASP.NET Core middleware (`app.Use(...)`) and Minimal APIs.

### Path 2: The Legacy "Appliance" Gateway

This approach treats ConduitSharp like a standalone infrastructure appliance (similar to Kong or Envoy). It is ideal for Operations or DevOps teams who want to manage an API gateway without writing C# wrapper code.

**Why choose this path?**

- **Dynamic Plugin System:** Extend the gateway by dropping compiled `.dll` files into the `plugins/` directory. The gateway will automatically scan and load them at runtime.
- **Zero Coding Required:** Deploy the pre-built ConduitSharp binary and run it purely via JSON.

---

## 📦 Installation

Pick by how you deploy. The embedded-library and Docker paths fit immutable-infrastructure workflows; the bare-metal / Windows Service / IIS options (the legacy-estate differentiator) live in the [in-depth docs](#-documentation).

### Embedded library (NuGet) — recommended

Host the gateway *inside your own ASP.NET Core app* — the YARP `AddReverseProxy()` / `MapReverseProxy()` model. Plugins arrive as NuGet packages registered in DI, so the whole deployment is one immutable, versioned build.

```bash
# --prerelease while 1.0.0-rc is the latest; drop it once a stable 1.0.0 ships
dotnet add package ConduitSharp.Gateway.AspNetCore --prerelease
```

```csharp
using ConduitSharp.Gateway;

var builder = WebApplication.CreateBuilder(args);
builder.AddConduitSharpGateway(options =>
{
    options.PathPrefix = "/api";   // gateway owns /api/*; the rest of the app is yours
});

var app = builder.Build();
app.MapGet("/hello", () => "served by the host app");   // coexists with the gateway
app.UseConduitSharpGateway();
app.Run();
```

### Docker

```bash
docker run -p 5050:5050 \
  -v ./routes.json:/app/Configuration/routes.json \
  ghcr.io/liqngliz/conduitsharp:latest
```

To ship plugins the immutable way, build your own image `FROM` this one and `COPY` the published plugin DLLs into `/app/plugins/`.

### dotnet tool

```bash
# --prerelease while 1.0.0-rc is the latest; drop it once a stable 1.0.0 ships
dotnet tool install -g ConduitSharp.Gateway --prerelease
conduitsharp
```

Works on Windows, macOS, and Linux. Requires .NET 10 SDK.

---

## 🧭 Configuring routes

All routing lives in `Configuration/routes.json` — no database, no admin UI, just a file you commit, review, and diff.

```json
{
  "routes": [
    {
      "id": "user-service-route",
      "route": {
        "match": { "path": "/api/users/{**catch-all}", "methods": ["GET", "POST"] }
      },
      "cluster": {
        "loadBalancingPolicy": "RoundRobin",
        "destinations": {
          "node-0": { "address": "http://user-service-1:8080" },
          "node-1": { "address": "http://user-service-2:8080" }
        }
      },
      "retry":          { "maxAttempts": 2, "delayMs": 100 },
      "circuitBreaker": { "threshold": 5, "cooldownMs": 10000 },
      "plugins": [
        { "name": "jwt-auth",   "order": 1, "config": { "issuer": "https://auth.example.com" } },
        { "name": "rate-limit", "order": 2, "config": { "requestsPerWindow": 100 } },
        { "name": "http-proxy", "order": 99 }
      ]
    }
  ]
}
```

A route has two halves, and the split is deliberate:

- **`route` and `cluster` are YARP's own `RouteConfig` and `ClusterConfig`**, used verbatim — so *every* YARP feature (session affinity, active health checks, transforms, header/query matchers) is available the day YARP ships it. `routeId`, `clusterId`, and `order` are derived from the route's `id` and its position in the file, so you never type them.
- **Everything else is ConduitSharp's** — `retry`, `circuitBreaker`, `plugins`, `swagger`, `maxRequestBodyBytes` — because YARP has no concept of any of them.

Write it all in camelCase; YARP's records bind case-insensitively. The full field reference — load balancing policies, retry/circuit-breaker fields, path & query syntax — is in the [in-depth docs](#-documentation).

### Built-in plugins

| Name | What it does |
| --- | --- |
| `jwt-auth` | Validates HS256 Bearer JWTs; enforces exp, nbf, iss, aud, and optional claim-based RBAC (`requiredClaims`) |
| `jwks-jwt-auth` | Validates RS/ES Bearer JWTs via a remote JWKS endpoint (Auth0, Azure AD, Google, Keycloak) |
| `api-key-auth` | Validates API keys from a request header (plain-text comparison) |
| `api-key-auth-hashed` | Validates API keys by comparing SHA-256 hash; keys never stored raw |
| `rate-limit` | Fixed-window quota per route or per client header value |
| `cache` | Response caching with configurable TTL and vary-by-header rules |
| `header-transform` | Add, remove, or rewrite request headers before forwarding upstream |
| `http-proxy` | Not a plugin — names where in the chain YARP forwards upstream. Omit it and the forward is appended at the end of the chain |

### Shipped example plugins

Runnable extensions under [examples/](examples/) — copy the source as a template, or reference the NuGet package directly:

| Example | Kind | NuGet package | What it shows |
| --- | --- | --- | --- |
| [ConduitSharp.Plugin.PowerShell](examples/ConduitSharp.Plugin.PowerShell) | `IPipelinePlugin` (`custom` / `power-shell`) | `ConduitSharp.Plugin.PowerShell` | Runs an existing `.ps1` in-process via the embedded `Microsoft.PowerShell.SDK` — no system `pwsh` install |
| [ConduitSharp.Plugin.BodyCapture](examples/ConduitSharp.Plugin.BodyCapture) | `IPipelinePlugin` (`custom` / `body-capture-streaming`, `body-capture`) | `ConduitSharp.Plugin.BodyCapture` | Logs request bodies two ways: a bounded prefix teed off the streaming path via ASP.NET Core `HttpLogging` (`ReadsRequestBody => false`, no buffering), or the whole body via the gateway's buffer (`ReadsRequestBody => true`) — the two patterns for a payload-inspecting plugin |
| [ConduitSharp.Cache.RedisProtocol](examples/ConduitSharp.Cache.RedisProtocol) | `ICacheService` seam | `ConduitSharp.Cache.RedisProtocol` | Swaps the in-memory `cache` store for Redis/Valkey — shared response cache across instances |
| [ConduitSharp.RateLimit.RedisProtocol](examples/ConduitSharp.RateLimit.RedisProtocol) | `IRateLimitStore` seam | `ConduitSharp.RateLimit.RedisProtocol` | Swaps the in-memory `rate-limit` store for Redis/Valkey — shared quota across instances |
| [ConduitSharp.RateLimit.SlidingWindow](examples/ConduitSharp.RateLimit.SlidingWindow) | `IRateLimiter` seam | `ConduitSharp.RateLimit.SlidingWindow` | Swaps the fixed-window *algorithm* for a sliding log — refuses the 2x burst a fixed window allows across its boundary. The algorithm and the store are separate seams |

---

## 🔌 Writing a plugin

Plugins implement one interface (`IPipelinePlugin`) and can be written in **C#, F#, VB.NET, or PowerShell** — not Lua.

| Language | How |
|---|---|
| **C#** | Reference `ConduitSharp.Core`, implement `IPipelinePlugin`, drop the DLL in `plugins/` |
| **F# / VB.NET** | Same as C# — they compile to identical .NET IL |
| **PowerShell** | A C# shim hosts `Microsoft.PowerShell.SDK` and runs your `.ps1`; no rewrite of existing scripts |

`IPipelinePlugin.ExecuteAsync` **is** the ASP.NET Core middleware signature — `HttpContext`, the plugin's JSON config, and `next`. To short-circuit, write the response and don't call `next()`:

```csharp
public sealed class PowerShellPlugin : IPipelinePlugin
{
    public PluginName Name    => PluginName.Custom;
    public string?    Variant => "power-shell";
    public string     Id      => "power-shell";

    public async Task ExecuteAsync(HttpContext context, JsonElement config, RequestDelegate next)
    {
        var options = JsonSerializer.Deserialize<PsConfig>(config)
            ?? throw new InvalidOperationException("PowerShell plugin config is null.");

        using var ps = PowerShell.Create();
        ps.AddScript("$ErrorActionPreference = 'Stop'"); // treat all errors as terminating
        ps.AddScript(await File.ReadAllTextAsync(options.ScriptPath));
        ps.AddParameter("Request", context.Request);

        try
        {
            var results = await ps.InvokeAsync();
            await context.Response.WriteAsync(string.Join("\n", results.Select(r => r.ToString())));
        }
        catch (RuntimeException ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.Message); }
        catch (ParseException)      { context.Response.StatusCode = 500; await context.Response.WriteAsync("PowerShell script configuration error."); }
        // no next() — the script produced the response
    }
}
```

```json
{ "name": "custom", "variant": "power-shell", "order": 99, "config": { "scriptPath": "scripts/MyReport.ps1" } }
```

A ready-to-use build of this pattern lives at [examples/ConduitSharp.Plugin.PowerShell](examples/ConduitSharp.Plugin.PowerShell). Runspace pooling, out-of-process execution, and production guidance are in the [in-depth docs](#-documentation).

---

## ⚖️ Compared to alternatives

ConduitSharp can replace an existing gateway or sit behind one. Against Ocelot it competes directly. Rather than competing with YARP, it builds on top of it as the forwarding engine. Against Kong, APISIX, or Azure APIM it complements — those handle external edge traffic while ConduitSharp handles legacy workload execution and internal observability at the layer below.

|  | ConduitSharp | Ocelot | YARP | Kong | APISIX |
| --- | --- | --- | --- | --- | --- |
| **Language** | C# / .NET | C# / .NET | C# / .NET | Lua / Nginx | Lua / Nginx |
| **Plugin language** | **C# / F# / VB.NET / PowerShell**<br/> + ASP.NET middleware | None | ASP.NET middleware | Lua | Lua |
| **PowerShell script execution** | ✅ Plugin | ❌ | ❌ | ❌ | ❌ |
| **Per-route plugin pipeline** | ✅ | ⚠️ per-route hook <br/> (DelegatingHandler, code-registered) | ❌ | ✅ | ✅ |
| **Drop-in external plugins** | ✅ | ❌ | ❌ | ✅ Lua | ✅ Lua |
| **Plugin contract as NuGet** | ✅ | ❌ | ❌ | — | — |
| **Unit-testable without HTTP** | ✅ | ❌ | ❌ | — | — |
| **OpenTelemetry (traces + metrics)** | ✅ Built-in | ❌ | ✅ | ✅ Plugin | ✅ Plugin |
| **Forwarding engine** | **YARP native** | Custom | YARP native | Nginx | Nginx |
| **Windows Service / IIS native** | ✅ | ✅ | ✅ | ❌ | ❌ |
| **Config format** | JSON file | JSON file | JSON / YAML | Database | YAML / etcd |

---

## 📚 Documentation

The front page above covers getting started. The full reference lives in the in-depth docs:

- [Gateway settings](docs/GATEWAY_SETTINGS.md) — `appsettings.json`, env-var overrides, request-body budgets
- [Configuring routes — full reference](docs/ROUTING.md) — load balancing, path & query syntax, per-route limits
- [Retries and circuit breaking](docs/ROUTING.md#retries-and-circuit-breaking)
- [Claim-based authorization (RBAC)](docs/AUTHORIZATION.md) · [Microsoft Entra ID setup](docs/AUTHORIZATION.md#microsoft-entra-id-azure-ad--v20-token-app-role-rbac)
- [TLS / HTTPS and mTLS](docs/TLS.md)
- [Observability](docs/OBSERVABILITY.md)
- [Deployment patterns](docs/ARCHITECTURE.md#deployment-patterns) — edge · sidecar · per-Data-Product
- [Architecture](docs/ARCHITECTURE.md) — request lifecycle, package graph, key decisions
- [Bare-metal / Windows Service / IIS install](docs/DEPLOYMENT_BAREMETAL.md)
- [Migration guide](CHANGELOG.md#migrating-routesjson)

---

## 🤝 Contributing

Issues and PRs welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md). Report security issues via [SECURITY.md](SECURITY.md).

## 📄 License

Apache 2.0 — see [LICENSE](LICENSE).


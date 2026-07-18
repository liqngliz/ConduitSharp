# ConduitSharp Benchmark Plan

Status: planning. Not yet built. Created 2026-07-16.

## What Ocelot and APISIX actually do

**Ocelot — micro only.** In-repo `test/Ocelot.Benchmarks` using BenchmarkDotNet.
Component hot-paths (route finding, request mapping/serialization, pipeline build) → ns/op +
allocations. No published system-load QPS charts. Benchmarks are a regression guard, not marketing.

**APISIX — macro, published as marketing** ([benchmark page](https://apisix.apache.org/docs/apisix/benchmark/)):

- Tool: **wrk**
- HW: cloud VM (n1-highcpu-8), **gateway pinned to 4 cores**, load-gen + OS on the other 4
- Upstream: **nginx, 1 KB response**, 2 nodes round-robin
- **Scenario A**: pure proxy, **no plugins** (routing ceiling)
- **Scenario B**: **2 plugins** (limit-count + prometheus)
- Also a **fake/mock upstream** mode → raw request-processing ceiling, no backend interference
- Metrics: **QPS vs CPU cores**, **latency in μs** with percentiles

**Takeaway:** Ocelot proves *no regressions*; APISIX proves *it's fast* to buyers. ConduitSharp
should do **both** — steal Ocelot's micro rigor and APISIX's macro method (core pinning, 1 KB
upstream, plugins on/off, fake-upstream ceiling).

---

## Phase 1 — Microbenchmarks (Ocelot-style, BenchmarkDotNet)

`benchmarks/ConduitSharp.Benchmarks`, `dotnet run -c Release`. No infra.

**CI guard = B/op only.** Allocations are deterministic; ns/op on shared CI runners is noise and
false-alarms. CI asserts allocation budgets; timing numbers are local/manual runs on a quiet box.

| Bench | Proves / rebuts |
|---|---|
| Route match @ 1/10/100/500 routes | flat cost (endpoint-routing DFA) — rebuts the stale "O(N) RouteMatcher" critique |
| Plugin pipeline 0/1/N plugins | per-plugin overhead ~constant |
| Buffered vs `streamOnly` (1 KB / 1 MB / 10 MB body) | zero-alloc passthrough claim (B/op) |
| JWT validate + requiredClaims | auth hot-path cost |

Method notes:

- **Route match**: don't isolate `DfaMatcher` (fiddly, and it's Microsoft's code anyway) — in-proc
  `TestServer` end-to-end per route count. Same conclusion, less rig.
- **Buffered vs streamOnly**: `[MemoryDiagnoser]` + mock upstream `HttpMessageHandler`, else the
  bench measures sockets, not the gateway. Bench routes must not set retry (`maxAttempts > 1`) —
  retry + streamOnly is rejected at startup.

Report: ns/op + **B/op** (allocation is the honest column).

## Phase 2 — Macro load (APISIX-style, bombardier/wrk + docker-compose)

Copy APISIX's rig exactly so numbers are comparable: 4-core pin, 1 KB response, load-gen isolated.

**Prerequisite: Linux box (metal or cloud VM).** Docker on macOS runs in a VM — core pinning is
meaningless there, and numbers are only self-relative, not APISIX-comparable. Dev runs on Mac are
fine for smoke-testing the rig, never for published numbers.

Method rules:

- **Latency numbers from fixed-rate runs** — bombardier `--rate` (or wrk2). Saturating
  closed-loop runs (wrk default) suffer coordinated omission: p99 lies under saturation. Use
  saturating runs only for the max-QPS number.
- **Pin runtime config and publish it**: ServerGC on, ReadyToRun/AOT flags, Kestrel defaults —
  these swing macro numbers 2×. Same settings across ConduitSharp/YARP/Ocelot runs or the
  comparison is unfair in someone's direction. Plain HTTP (APISIX benches plain HTTP).

| Bench | Mirrors APISIX | Proves |
|---|---|---|
| Pure proxy, no plugins | Scenario A | routing/forward ceiling QPS + p99 |
| auth + rate-limit + cache chain | Scenario B (2 plugins) | cost of policy |
| Fake upstream (plugin-only route, no forward) | their mock mode | gateway's raw ceiling |
| Gateway vs direct-to-upstream | — | added-hop overhead % |
| Gateway vs bare YARP vs Ocelot | — | the money chart (same HW/upstream) |

Metrics: QPS, p50/p99 latency (μs, like APISIX), CPU/mem.

## Phase 3 — Resource / GC (dotnet-counters)

| Bench | Proves |
|---|---|
| Upload-flood → heap bounded, 503 load-sheds | the `RequestBodyBudget` memory-bounded claim — directly |
| 30-min soak | no leak, stable working set, GC pauses |

---

## Deliverables

- `benchmarks/ConduitSharp.Benchmarks/` — BenchmarkDotNet (Phase 1)
- `benchmarks/load/` — docker-compose (gateway + nginx-1KB + bombardier) + run script → emits markdown table (Phase 2/3)
- `docs/BENCHMARKS.md` — method + results + the comparison chart

## Sequencing (lazy → high-value first)

1. **Phase 1 route-scaling + buffered-vs-stream** — infra-free, kills the O(N) critique, proves zero-alloc. Ship first.
2. **Phase 2 Scenario A/B + fake-upstream** — APISIX-comparable QPS numbers.
3. **Phase 2 vs-Ocelot/vs-YARP** — marketing chart.
4. **Phase 3** — GC/memory-bound proof.

## Notes / facts verified

- ConduitSharp routing is ASP.NET Core endpoint routing (`RouteEndpointBuilder` + O(1) route-by-id
  dictionary in `GatewayRouteTable`), not a custom linear matcher. The "O(N) routing" critique is
  stale — Phase 1 route-scaling proves it.
- `streamOnly` routes bypass buffering; buffered routes are capped by `Gateway:RequestLimits`
  (`MaxRequestBodyBytes` per-request 413, `MaxTotalBufferedBodyBytes` global 503 load-shed).

## Sources

- APISIX benchmark: https://apisix.apache.org/docs/apisix/benchmark/
- APISIX vs Envoy method: https://apisix.apache.org/blog/2021/06/10/apache-apisix-and-envoy-performance-comparison/
- Ocelot repo: https://github.com/ThreeMammals/Ocelot
- BenchmarkDotNet: https://benchmarkdotnet.org/

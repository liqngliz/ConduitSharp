# Gateway settings

_Part of the [ConduitSharp documentation](../README.md)._


All gateway settings live in `Configuration/appsettings.json` next to the binary. Every value can be overridden with an environment variable using the double-underscore separator — no config file edits needed in containers or CI.

```json
{
  "Gateway": {
    "RoutesPath": "Configuration/routes.json",
    "AdminKeyHash": "",
    "Observability": {
      "Otlp": {
        "Enabled": false,
        "Endpoint": "http://localhost:4317"
      }
    },
    "Tls": {
      "ClientCertificates": []
    },
    "RequestLimits": {
      "MaxRequestBodyBytes": 8388608,
      "MaxDiskBufferedBodyBytes": 67108864,
      "MaxRamBufferedBodyBytes": 67108864,
      "RamBufferThresholdBytes": 1048576,
      "SpillDirectory": null
    }
  }
}
```

| Setting | Env var override | Default |
|---|---|---|
| `Gateway.RoutesPath` | `Gateway__RoutesPath` | `Configuration/routes.json` |
| `Gateway.AdminKeyHash` | `Gateway__AdminKeyHash` | *(disabled)* |
| `Gateway.Observability.Otlp.Enabled` | `Gateway__Observability__Otlp__Enabled` | `false` |
| `Gateway.Observability.Otlp.Endpoint` | `Gateway__Observability__Otlp__Endpoint` | *(OTel SDK default)* |
| `Gateway.RequestLimits.MaxRequestBodyBytes` | `Gateway__RequestLimits__MaxRequestBodyBytes` | `8388608` (8 MiB) |
| `Gateway.RequestLimits.MaxRamBufferedBodyBytes` | `Gateway__RequestLimits__MaxRamBufferedBodyBytes` | `67108864` (64 MiB) |
| `Gateway.RequestLimits.MaxDiskBufferedBodyBytes` | `Gateway__RequestLimits__MaxDiskBufferedBodyBytes` | `67108864` (64 MiB) |
| `Gateway.RequestLimits.RamBufferThresholdBytes` | `Gateway__RequestLimits__RamBufferThresholdBytes` | `1048576` (1 MiB) |
| `Gateway.RequestLimits.SpillDirectory` | `Gateway__RequestLimits__SpillDirectory` | *(system temp path)* |

### Which body limit is which

Five settings bound request bodies and they are easy to confuse, because they meter **different
resources** and fire **different outcomes**:

| Setting | Applies to | Meters | Exceeding it means | Size it against |
|---|---|---|---|---|
| `MaxRequestBodyBytes` | one request | body size | **413** to the client | your API contract — how big a body you accept |
| `MaxRamBufferedBodyBytes` | all in-flight, combined | **RAM** — buffered bodies + capture prefixes | a buffered body spills to disk; a capture reservation that will not fit sheds **503** | memory available to the process |
| `MaxDiskBufferedBodyBytes` | all in-flight, combined | **spill-file bytes** | **503**, the load-shed | free space on `SpillDirectory` |
| `RamBufferThresholdBytes` | one body | RAM, before it spills | that body spills to disk | leave at default unless large bodies must avoid disk |
| `SpillDirectory` | — | *where* spilled bytes go | — | pick tmpfs vs real storage deliberately (see below) |

**One budget per physical resource, and they are independent on purpose.** Raising the disk budget
to suit a large spill volume must not silently enlarge what may be held in RAM — that is how a
gateway gets OOM-killed instead of shedding. Set each against the resource it actually meters.

A buffered body is charged to exactly one budget at a time: RAM while it fits the threshold, disk
once it spills. Body-capture is different — its prefix is RAM-only and never spills, so the gateway
**reserves** it against the RAM budget up front and sheds a **503** if the reservation will not fit,
rather than spilling. A route capturing both directions reserves `request.maxSize + response.maxSize`
(see the [body-capture plugin](../examples/ConduitSharp.Plugin.BodyCapture/README.md#memory-and-disk)),
so enabling response capture roughly halves the concurrency at which it starts shedding.

> **The tmpfs trap.** If `SpillDirectory` resolves to a `tmpfs` mount — `/tmp` often is inside
> containers — then "disk" *is* RAM and `MaxDiskBufferedBodyBytes` becomes a second memory budget.
> See [Spilling to tmpfs](#tuning-the-buffered-path) below for the limit ordering that keeps overload
> a 503 rather than a 500 or an OOM-kill.

Note that `BodyCapture:MaxCaptureBytes` is **not** in this family: it bounds how much of a body gets
*logged*, not how much is buffered for the forward. See the body-capture plugin's README.

### When buffering happens at all

A request body is buffered only when something on the route consumes the buffer — a retry
policy (idempotent methods only, or non-idempotent too if the route sets `retryNonIdempotent`)
or a body-reading plugin; every other request streams straight through.

When a body *is* buffered, buffering degrades in two tiers rather than one step:

1. **RAM**, while `MaxRamBufferedBodyBytes` has headroom. Each body gets up to
   `RamBufferThresholdBytes` of heap; this is ~3–5x faster than spilling.
2. **Disk**, once the RAM tier is full. Further bodies spill to a temp file from the first
   byte — slower, but still served.
3. **503**, only when `MaxDiskBufferedBodyBytes` has no room either — neither resource can take it.

When a body spills, its bytes move from the RAM budget to the disk budget: the rented buffer goes
back to the pool, so that RAM is genuinely free for the next request. Nothing is double-counted.

The RAM budget is why `RamBufferThresholdBytes` can be generous per request without memory running
away: a body is only granted the threshold if the RAM budget still has headroom, so the per-body
number can never lift total RAM above the budget. A body whose `Content-Length` already exceeds the
threshold skips the RAM buffer entirely and spills from the first byte, since filling a buffer only
to copy it to disk helps nobody.

`RamBufferThresholdBytes` is floored at 4 KiB with no upper cap, but 1 MiB is a real inflection
point: `FileBufferingReadStream` serves thresholds up to 1 MiB from `ArrayPool`, and above it grows
a bare `MemoryStream` by doubling — allocating roughly 2x the body on the Large Object Heap. Raising
it past 1 MiB is a deliberate trade: bodies that fit in the raised ceiling skip the disk round-trip
entirely, paid for in LOH allocation. Raise it only alongside a `MaxRamBufferedBodyBytes` that can
actually cover the concurrency you expect.

`MaxRequestBodyBytes` rejects an individual oversized buffered request with `413`; a route's
own `"maxRequestBodyBytes"` (see [Configuring routes](ROUTING.md)) overrides it per route. The
limit is handed to the server (Kestrel) on both paths — streaming and buffered — so the
configured value *is* the transport limit; the buffered path additionally re-checks it while
reading, as the backstop for chunked bodies with no `Content-Length`. `0` disables the limit on
both paths (genuinely unlimited); a negative value leaves the server's own default in place
(Kestrel: ~28.6 MiB). Note that `0` means something different on `MaxRequestBodyBytes` than on the
two buffering budgets: here it disables the limit, whereas on a budget it means *none of that
resource*. The budgets have no "unlimited" value — for effectively unbounded buffering set a number
large enough never to bind. This is the one place the v2.0.0 rename inverts a meaning rather than
just moving it: pre-2.0.0 `MaxTotalBufferedBodyBytes: 0` meant unlimited.

Defaults are sized for a small container (256–512 MiB), not for a development host: at most
64 MiB of RAM and 64 MiB of spill across all in-flight buffered bodies.

### Tuning the buffered path

Buffering throughput is dominated by two settings, both worth far more than anything in the code.
Measured on the load rig's dedicated-box run (1 MB `PUT` on a retry route, c=96, median of 3 runs,
spread ≤±5%) — absolute QPS is that box's; the ratios are what travel, and the CI matrix
reproduces the ordering:

| | QPS |
|---|---:|
| everything forced to spill to **disk** (16 KiB threshold) | 1079 |
| everything forced to spill to a sized **`tmpfs`** | ~2500 |
| defaults — 1 MiB threshold, 64 MiB RAM tier absorbs ~91% of bodies | **6213** |
| *for scale:* APISIX on the same rig, same load (buffers every body) | 4960–5044 |

- **Keep bodies in the RAM tier.** That is the whole design: with defaults, ~91% of bodies at c=96
  never touched storage and the gateway outran APISIX; forced entirely onto disk it ran ~4.6x
  behind. A body qualifies if it is no larger than `RamBufferThresholdBytes` (≤ 1 MiB) *and*
  `MaxRamBufferedBodyBytes` has headroom — at 1 MB per body a 64 MiB tier covers ~64 in flight.
  Size the tier against the pod: a tier near the container limit thrashes the GC (.NET's heap hard
  limit is 75% of the container's memory) and is far worse than spilling.
- **When bodies must spill, the storage is the speed.** Container overlayfs and a mounted volume
  measure the same as each other; a sized `tmpfs` is a large multiple of both. The disk-spill path
  itself is the gateway's slowest: nginx writes request bodies inline in its event loop, while .NET
  has no true async file I/O on Unix and dispatches every spill write to the thread pool. The gap
  closes by keeping bodies out of the disk tier, not by tuning it.

**Spilling to `tmpfs`: get the limit order right.** Four separate limits can stop a RAM-backed
spill, and only one of them fails gracefully:

| Limit | Default | What happens when it binds |
|---|---|---|
| `MaxDiskBufferedBodyBytes` | 64 MiB | **503** — the gateway sheds deliberately |
| tmpfs mount `size=` | **half the host's RAM** if unset | `ENOSPC` → the spill write throws → **500** |
| container memory limit (cgroup) | none | **OOM-kill** — tmpfs pages are charged to the cgroup |
| `/dev/shm` | **64 MB** in Docker | `ENOSPC` → **500** |

So size them in this order:

```
MaxDiskBufferedBodyBytes  <  tmpfs size=  <  (container memory limit − heap headroom)
```

The budget must be the binding constraint, because it is the only limit that turns overload into a
retryable 503 rather than a 500 or a dead process. Get the order wrong and the symptom is
distinctive: a flood of fast 500s (tmpfs full) or a container that simply vanishes (cgroup). Note
that `--memory-swap` defaults to twice `--memory` in Docker, which can mask a cgroup overrun as
mysterious slowness instead of a kill; Kubernetes usually has swap off, where it is a clean kill.

**The `tmpfs` trade.** `/tmp` is `tmpfs` — RAM — on many container images, and that cuts both ways:

- It makes the disk tier fast, and `MaxDiskBufferedBodyBytes` still bounds it. Spilling to `tmpfs`
  with a total budget that fits in the pod is a legitimate, deliberate, fast configuration.
- It does **not** relieve memory pressure, because the "disk" tier is RAM. If your total budget is
  sized assuming spill lands on real storage, pointing it at `tmpfs` converts the step-down into an
  OOM — the process dies where it would otherwise have degraded and shed.

So: choose `tmpfs` for speed *with a budget that fits in memory*, or real storage for capacity, and
set `SpillDirectory` explicitly either way rather than inheriting whatever `/tmp` happens to be.

The `Kestrel` section (ports, inbound TLS cert) follows standard ASP.NET Core configuration — see the [TLS section](TLS.md) below.

---


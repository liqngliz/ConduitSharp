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
      "MaxTotalBufferedBodyBytes": 134217728,
      "MaxMemoryBufferedBodyBytes": 67108864,
      "MemoryBufferThresholdBytes": 1048576,
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
| `Gateway.RequestLimits.MaxTotalBufferedBodyBytes` | `Gateway__RequestLimits__MaxTotalBufferedBodyBytes` | `134217728` (128 MiB) |
| `Gateway.RequestLimits.MaxMemoryBufferedBodyBytes` | `Gateway__RequestLimits__MaxMemoryBufferedBodyBytes` | `67108864` (64 MiB) |
| `Gateway.RequestLimits.MemoryBufferThresholdBytes` | `Gateway__RequestLimits__MemoryBufferThresholdBytes` | `1048576` (1 MiB) |
| `Gateway.RequestLimits.SpillDirectory` | `Gateway__RequestLimits__SpillDirectory` | *(system temp path)* |

A request body is buffered only when something on the route consumes the buffer — a retry
policy (idempotent methods only) or a body-reading plugin; every other request streams
straight through.

When a body *is* buffered, buffering degrades in two tiers rather than one step:

1. **RAM**, while `MaxMemoryBufferedBodyBytes` has headroom. Each body gets up to
   `MemoryBufferThresholdBytes` of heap; this is ~3–5x faster than spilling.
2. **Disk**, once the RAM tier is full. Further bodies spill to a temp file from the first
   byte — slower, but still served.
3. **503**, only when `MaxTotalBufferedBodyBytes` (RAM + spill combined) is exhausted.

The memory tier is carved *out of* the total, not added to it, so `MaxTotalBufferedBodyBytes`
remains the worst-case ceiling. It is what bounds buffering's RAM footprint — which is why
`MemoryBufferThresholdBytes` can be generous per request without the aggregate running away.
A body whose `Content-Length` already exceeds the threshold skips the RAM buffer entirely
and spills from the first byte, since filling a buffer only to copy it to disk helps nobody.

`MemoryBufferThresholdBytes` is clamped to `[4 KiB, 1 MiB]`. The upper bound is not taste:
`FileBufferingReadStream` serves thresholds up to 1 MiB from `ArrayPool`, and above it grows
a bare `MemoryStream` by doubling — allocating roughly 2x the body on the Large Object Heap.

`MaxRequestBodyBytes` rejects an individual oversized buffered request with `413`; a route's
own `"maxRequestBodyBytes"` (see [Configuring routes](ROUTING.md)) overrides it per route. The
limit is handed to the server (Kestrel) on both paths — streaming and buffered — so the
configured value *is* the transport limit; the buffered path additionally re-checks it while
reading, as the backstop for chunked bodies with no `Content-Length`. `0` disables the limit on
both paths (genuinely unlimited); a negative value leaves the server's own default in place
(Kestrel: ~28.6 MiB). The two buffering budgets disable in opposite directions: no total means
*unlimited* buffering, while no memory tier means *no RAM at all* (every body spills).

Defaults are sized for a small container (256–512 MiB), not for a development host: at most
64 MiB of RAM across all in-flight buffered bodies, 128 MiB worst case including spill.

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
  behind. A body qualifies if it is no larger than `MemoryBufferThresholdBytes` (≤ 1 MiB) *and*
  `MaxMemoryBufferedBodyBytes` has headroom — at 1 MB per body a 64 MiB tier covers ~64 in flight.
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
| `MaxTotalBufferedBodyBytes` | 128 MiB | **503** — the gateway sheds deliberately |
| tmpfs mount `size=` | **half the host's RAM** if unset | `ENOSPC` → the spill write throws → **500** |
| container memory limit (cgroup) | none | **OOM-kill** — tmpfs pages are charged to the cgroup |
| `/dev/shm` | **64 MB** in Docker | `ENOSPC` → **500** |

So size them in this order:

```
MaxTotalBufferedBodyBytes  <  tmpfs size=  <  (container memory limit − heap headroom)
```

The budget must be the binding constraint, because it is the only limit that turns overload into a
retryable 503 rather than a 500 or a dead process. Get the order wrong and the symptom is
distinctive: a flood of fast 500s (tmpfs full) or a container that simply vanishes (cgroup). Note
that `--memory-swap` defaults to twice `--memory` in Docker, which can mask a cgroup overrun as
mysterious slowness instead of a kill; Kubernetes usually has swap off, where it is a clean kill.

**The `tmpfs` trade.** `/tmp` is `tmpfs` — RAM — on many container images, and that cuts both ways:

- It makes the disk tier fast, and `MaxTotalBufferedBodyBytes` still bounds it. Spilling to `tmpfs`
  with a total budget that fits in the pod is a legitimate, deliberate, fast configuration.
- It does **not** relieve memory pressure, because the "disk" tier is RAM. If your total budget is
  sized assuming spill lands on real storage, pointing it at `tmpfs` converts the step-down into an
  OOM — the process dies where it would otherwise have degraded and shed.

So: choose `tmpfs` for speed *with a budget that fits in memory*, or real storage for capacity, and
set `SpillDirectory` explicitly either way rather than inheriting whatever `/tmp` happens to be.

The `Kestrel` section (ports, inbound TLS cert) follows standard ASP.NET Core configuration — see the [TLS section](TLS.md) below.

---


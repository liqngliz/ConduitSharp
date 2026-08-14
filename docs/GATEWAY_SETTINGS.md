# Gateway settings

_Part of the [ConduitSharp documentation](../README.md)._


All gateway settings live in `Configuration/appsettings.json` next to the binary. Every value takes an environment-variable override using the double-underscore separator.

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

Five settings bound request bodies, each metering a different resource:

| Setting | Applies to | Meters | Exceeding it means | Size it against |
|---|---|---|---|---|
| `MaxRequestBodyBytes` | one request | body size | **413** to the client | your API contract |
| `MaxRamBufferedBodyBytes` | all in-flight, combined | RAM: buffered bodies + capture prefixes | body spills to disk; a capture reservation that will not fit sheds **503** | process memory |
| `MaxDiskBufferedBodyBytes` | all in-flight, combined | spill-file bytes | **503**, the load-shed | free space on `SpillDirectory` |
| `RamBufferThresholdBytes` | one body | RAM, before it spills | that body spills to disk | leave at default unless large bodies must avoid disk |
| `SpillDirectory` | | *where* spilled bytes go | | tmpfs vs real storage (see below) |

**One budget per physical resource, independent on purpose.** Raising the disk budget for a large
spill volume must not enlarge what may sit in RAM; that path ends in an OOM-kill instead of a shed.

A buffered body is charged to one budget at a time: RAM while it fits the threshold, disk once it
spills.

These budgets bound the gateway's **own** buffering. A plugin holding RAM of its own (the
`body-capture` prefix) reserves against the same `MaxRamBufferedBodyBytes` **additively**, on top of
buffering, and sheds **503** if it will not fit; being RAM-only, it never spills. For body-capture,
size this budget for buffering plus prefixes. See the
[body-capture plugin README](../plugins/ConduitSharp.Plugin.BodyCapture/README.md#memory-and-disk).

> **The tmpfs trap.** When `SpillDirectory` resolves to a `tmpfs` mount (`/tmp` often is, inside
> containers), "disk" *is* RAM and `MaxDiskBufferedBodyBytes` becomes a second memory budget.
> See [Spilling to tmpfs](#tuning-the-buffered-path) for the limit ordering that keeps overload
> a 503 rather than a 500 or an OOM-kill.

`BodyCapture:MaxCaptureBytes` is **not** in this family: it bounds how much of a body gets *logged*,
not how much is buffered for the forward. See the body-capture plugin's README.

### When buffering happens at all

A request body is buffered only when something on the route consumes the buffer: a retry policy
(idempotent methods only, or non-idempotent too if the route sets `retryNonIdempotent`) or a
body-reading plugin. Every other request streams straight through.

A buffered body degrades through two tiers, then sheds:

1. **RAM**, while `MaxRamBufferedBodyBytes` has headroom. Each body gets up to
   `RamBufferThresholdBytes` of heap, ~3-5x faster than spilling.
2. **Disk**, once the RAM tier is full. Further bodies spill to a temp file from the first byte.
3. **503**, when `MaxDiskBufferedBodyBytes` has no room either.

On spill, the bytes move from the RAM budget to the disk budget and the rented buffer returns to
the pool. Nothing is double-counted.

A body is granted the threshold only if the RAM budget has headroom, so a generous
`RamBufferThresholdBytes` can never lift total RAM above the budget. A body whose `Content-Length`
already exceeds the threshold skips the RAM buffer and spills from the first byte.

`RamBufferThresholdBytes` floors at 4 KiB, no upper cap, with a real inflection at 1 MiB:
`FileBufferingReadStream` serves thresholds up to 1 MiB from `ArrayPool`; above it, a bare
`MemoryStream` grows by doubling, allocating roughly 2x the body on the Large Object Heap. Bodies
fitting a raised ceiling skip the disk round-trip, paid for in LOH allocation. Raise it only
alongside a `MaxRamBufferedBodyBytes` that covers the expected concurrency.

`MaxRequestBodyBytes` rejects an oversized buffered request with `413`; a route's own
`"maxRequestBodyBytes"` (see [Configuring routes](ROUTING.md)) overrides it per route. Kestrel
receives the limit on both the streaming and buffered paths, so the configured value *is* the
transport limit; the buffered path re-checks while reading, as the backstop for chunked bodies with
no `Content-Length`.

`0` and negatives differ per setting:

| Value | On `MaxRequestBodyBytes` | On a buffering budget |
|---|---|---|
| `0` | limit disabled, genuinely unlimited | none of that resource |
| negative | Kestrel's own default (~28.6 MiB) | |

The budgets have no "unlimited" value; for effectively unbounded buffering, set a number large
enough never to bind. This is the one place the v2.0.0 rename inverts a meaning rather than moving
it: pre-2.0.0 `MaxTotalBufferedBodyBytes: 0` meant unlimited.

Defaults are sized for a small container (256-512 MiB), not a development host: at most 64 MiB of
RAM and 64 MiB of spill across all in-flight buffered bodies.

### Tuning the buffered path

`RamBufferThresholdBytes` and `SpillDirectory` dominate buffering throughput, both worth more than
anything in the code. Measured on the load rig's dedicated-box run (1 MB `PUT` on a retry route,
c=96, median of 3 runs, spread ≤±5%). Absolute QPS is that box's; the ratios travel, and the CI
matrix reproduces the ordering.

| | QPS |
|---|---:|
| everything forced to spill to **disk** (16 KiB threshold) | 1079 |
| everything forced to spill to a sized **`tmpfs`** | ~2500 |
| defaults: 1 MiB threshold, 64 MiB RAM tier absorbs ~91% of bodies | **6213** |
| *for scale:* APISIX on the same rig, same load (buffers every body) | 4960-5044 |

- **Keep bodies in the RAM tier.** On defaults, ~91% of bodies at c=96 never touched storage and the
  gateway outran APISIX; forced entirely onto disk it ran ~4.6x behind. A body qualifies at
  ≤ `RamBufferThresholdBytes` (≤ 1 MiB) *and* `MaxRamBufferedBodyBytes` headroom; at 1 MB per body a
  64 MiB tier covers ~64 in flight. Size the tier against the pod: near the container limit it
  thrashes the GC (.NET's heap hard limit is 75% of container memory) and loses to spilling.
- **When bodies must spill, the storage is the speed.** Container overlayfs and a mounted volume
  measure the same; a sized `tmpfs` is a large multiple of both. The disk-spill path is the
  gateway's slowest: nginx writes request bodies inline in its event loop, while .NET has no true
  async file I/O on Unix and dispatches every spill write to the thread pool. Close the gap by
  keeping bodies out of the disk tier, not by tuning it.

**Spilling to `tmpfs`: get the limit order right.** Four limits can stop a RAM-backed spill; one
fails gracefully.

| Limit | Default | What happens when it binds |
|---|---|---|
| `MaxDiskBufferedBodyBytes` | 64 MiB | **503**, a deliberate shed |
| tmpfs mount `size=` | **half the host's RAM** if unset | `ENOSPC` → spill write throws → **500** |
| container memory limit (cgroup) | none | **OOM-kill**; tmpfs pages are charged to the cgroup |
| `/dev/shm` | **64 MB** in Docker | `ENOSPC` → **500** |

Size them in this order:

```
MaxDiskBufferedBodyBytes  <  tmpfs size=  <  (container memory limit − heap headroom)
```

The budget must be the binding constraint: it is the only one of the four that turns overload into
a retryable 503. Wrong order has a distinctive symptom, a flood of fast 500s (tmpfs full) or a
container that vanishes (cgroup). Docker's `--memory-swap` defaults to twice `--memory`, which can
mask a cgroup overrun as slowness instead of a kill; Kubernetes usually has swap off, where it is a
clean kill.

**The `tmpfs` trade.** `/tmp` is `tmpfs`, meaning RAM, on many container images:

- Fast disk tier, still bounded by `MaxDiskBufferedBodyBytes`. Spilling to `tmpfs` with a total
  budget that fits in the pod is a legitimate configuration.
- No relief from memory pressure, because the "disk" tier is RAM. A budget sized for real storage,
  pointed at `tmpfs`, turns the step-down into an OOM: the process dies where it would have shed.

Choose `tmpfs` for speed *with a budget that fits in memory*, or real storage for capacity. Set
`SpillDirectory` explicitly either way, rather than inheriting whatever `/tmp` happens to be.

The `Kestrel` section (ports, inbound TLS cert) follows standard ASP.NET Core configuration. See
[TLS](TLS.md).

---


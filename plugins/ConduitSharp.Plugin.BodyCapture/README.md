# ConduitSharp BodyCapture Plugin

Plugins for the [ConduitSharp](https://github.com/liqngliz/ConduitSharp) API gateway that capture incoming HTTP request bodies and export them via structured OpenTelemetry logging — so you can ingest and search request bodies in downstream stacks like Grafana Loki.

One plugin ships here, variant `body-capture`. It never forces the gateway to buffer
(`ReadsRequestBody => false`) and picks the cheaper of two paths per request:

| Route shape | Path taken | Cost |
| :--- | :--- | :--- |
| Body already buffered — a retry route, or a body-reading plugin on the same route | Reads the prefix off the gateway's existing seekable buffer and rewinds | No second copy. Reads raw bytes, so binary bodies are captured too |
| Plain streaming route | Tees a bounded prefix through ASP.NET Core `HttpLogging` | The prefix only, never the body. Text-ish media types only |

> **Upgrading from v1.x:** the separate `body-capture-streaming` variant is gone — use
> `body-capture`. The old buffering `BodyCapturePlugin` (which declared `ReadsRequestBody => true`
> and forced whole-body buffering on every route) has been removed; the reuse path above covers the
> case it existed for, without the forced buffering.

## Installation

Drop the compiled plugin DLL into your gateway's configured `PluginsPath` (e.g. `./gateway/plugins`).

## How it works

Declares `ReadsRequestBody = false`, so the route stays on the gateway's zero-copy streaming path — no memory buffer, no temp-file spill. Under the hood it runs ASP.NET Core's own `HttpLogging` middleware, whose `RequestBufferingStream` tees the first `maxSize` bytes into pooled memory as YARP streams the body upstream and drops the rest.

Heap cost is the captured prefix, never the body: a 10 MB upload and a 1 KB one allocate the same ~26 KB per request.

### Example `routes.json`

```json
{
  "id": "my-service",
  "route": {
    "match": { "path": "/api/myservice", "methods": ["POST"] }
  },
  "plugins": [
    {
      "name": "custom",
      "variant": "body-capture",
      "order": 10,
      "enabled": true,
      "config": {
        "request":  { "maxSize": 4096 },
        "response": { "maxSize": 8192 }
      }
    },
    {
      "name": "http-proxy",
      "order": 99,
      "enabled": true
    }
  ]
}
```

### Options

Config is nested by direction. A direction is captured only when its block is present with a positive
`maxSize`. Omit the block (or set `maxSize: 0`) to skip it.

| Setting | Type | Required | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| `request.maxSize` | `integer` | No | none (off) | Bytes of the **request** body to log. Present block with no `maxSize` uses 4 KiB. Longer bodies log truncated. |
| `response.maxSize` | `integer` | No | none (off) | Bytes of the **response** body to log. Same rules. |

Request capture picks the cheap path: it reads off the gateway's buffer on a retry/buffered route
(raw bytes, binary included), and tees via HttpLogging on a plain streaming route (text media types
only). Response capture is a bounded write-through tee on `Response.Body`, so it runs on every route
and captures binary responses too.

Each direction's `maxSize` is capped by a gateway-wide ceiling (`BodyCapture:MaxCaptureBytes`, default
32 KiB); a larger value is rejected at startup.

> **Migrating from 1.x:** the flat `{ "maxSize": N }` shape is removed and rejected at startup. Wrap
> it: `{ "request": { "maxSize": N } }`.

#### Why there is a ceiling at all

A captured prefix is held in RAM and never spills, so it is charged to
`Gateway:RequestLimits:MaxRamBufferedBodyBytes` — a larger prefix consumes that budget faster and
sheds (503) sooner. 32 KiB also keeps each prefix under the 85 KiB large object heap threshold (and
matches HttpLogging's own `RequestBodyLogLimit` default), so capture stays on pooled, gen-0-sized
buffers no matter how many requests are in flight.

Raise `BodyCapture:MaxCaptureBytes` only alongside a RAM budget that can cover it at your real
concurrency. If you need whole bodies, send them to a dedicated audit sink
(`body-capture-file`) rather than routing full payloads through your log pipeline.

### Memory and disk

Capture is **RAM-only** — a streaming tee holds pooled segments, a reuse-path read rents from
`ArrayPool`, and neither ever spills. So it is metered against the gateway's **RAM** budget, never
its disk budget:

| Setting | Where | Governs |
| :--- | :--- | :--- |
| `request.maxSize` / `response.maxSize` | `routes.json` | bytes captured per direction — the per-request RAM this plugin reserves |
| `BodyCapture:MaxCaptureBytes` | gateway config | per-direction ceiling (default 32 KiB) |
| `Gateway:RequestLimits:MaxRamBufferedBodyBytes` | gateway config | the aggregate RAM budget the reservation counts against; capture sheds (503) when exhausted |

The gateway reserves `request.maxSize + response.maxSize` against `MaxRamBufferedBodyBytes` for the
life of each captured request (declared via `CaptureMemoryBytes`), so a connection flood sheds with a
503 instead of growing capture memory unbounded. The sum, not the max: HttpLogging holds the request
prefix until the response completes, so both are live at once. `MaxDiskBufferedBodyBytes` is **not**
involved — capture writes no spill files. Turning on response capture roughly doubles the reservation,
so a route capturing both sheds at about half the concurrency of a request-only route.

This reservation is **additive**, not a separate budget. It stacks on top of whatever RAM the gateway
is already using to buffer the request body (for a retry loop, or a body-reading plugin on the route),
all under the one `MaxRamBufferedBodyBytes` ceiling. On a retry route that both buffers and captures,
the buffered body and the capture prefixes draw from the same budget, so size that budget for the sum.
The gateway-core buffering budgets themselves are documented in
[Request body limits](../../docs/GATEWAY_SETTINGS.md); this plugin only adds its declared prefix on top.

See [Request body limits](../../docs/GATEWAY_SETTINGS.md) for the two-budget model, and
[Observability → body capture and log level](../../docs/OBSERVABILITY.md) for what makes captured
bodies actually leave the process.

### What gets captured, and what deliberately does not

Capture is **scoped to the body's potential cause**: on the streaming path the body is logged only
when it was actually forwarded — read by the forwarder. That is not a limitation, it is the design.
A request whose body never reached upstream is not one the body could explain:

| outcome | body captured (streaming path)? | why |
|---|---|---|
| forwarded, upstream returned any status (2xx…5xx) | **yes** | the payload was sent; it can be the cause |
| upstream unreachable / connection refused or dropped before the body was read | **no** | transport/availability failure — the body is not the cause and not needed to diagnose it |
| rejected by an earlier plugin (401 / 403 auth, rate-limit) | **no** | the rejection is the cause; the payload is not. In the shipped order (auth `order: 1`, capture `order: 10`) capture never runs for a rejected request |

This keeps the log to bodies you acted on, rather than storing every payload anyone sent — including
rejected, malformed, or never-forwarded ones, which is added liability (PII, secrets, attacker
payloads) and volume for no diagnostic gain.

If you specifically need to **audit rejected payloads** (attack-forensics), that is a deliberate,
scoped choice, not the default: order this plugin *before* auth **and** run it on a buffered/retry
route so it takes the reuse path (which logs the prefix before the forward). On a plain streaming
route it cannot capture a rejected body, by construction.

### Behaviour inherited from HttpLogging (streaming path only)

These apply when the route is streaming and the tee is used. On the reuse path the plugin reads raw
bytes itself, so none of them apply — binary bodies included.

- **Only recognized media types are logged.** `application/json`, `text/*`, `application/xml`, and `application/*+json` are captured; `application/octet-stream` and other binary types stream through untouched and unlogged. Configure via HttpLogging's `MediaTypeOptions`.
- **Truncation is explicit.** An over-long body logs the prefix plus `RequestBodyStatus: [Truncated by RequestBodyLogLimit]`.
- **Requests with no `Content-Type` are not logged** (a `Debug`-level `No Content-Type header for request body` is emitted instead).

## Observability

Both paths log at `Information` through the host's `ILoggerFactory`, so records flow out through whatever it has wired up — including the gateway's OpenTelemetry logger provider (→ OTLP → Loki), enriched with the ambient trace/route attributes.

| Path | Category | Message |
| :--- | :--- | :--- |
| Streaming (tee) | `ConduitSharp.Plugin.BodyCapture.StreamingBodyCapturePlugin` | `Request and Response: RequestBody: …` / `RequestBodyStatus: …` |
| Reuse (already buffered) | `ConduitSharp.Plugin.BodyCapture.StreamingBodyCapturePlugin` | `Captured request body for path {Path} route {RouteId}: {Body}` |

Both land under one category. The tee path deliberately re-homes HttpLogging's loggers under its own category. HttpLogging natively logs under `Microsoft.AspNetCore.HttpLogging.*`, and every stock ASP.NET Core log config filters `Microsoft.AspNetCore` to `Warning` — which would drop every captured body silently while the plugin still looked healthy. Renaming means capture obeys the plugin's own log level, and the records land in Loki tagged as body-capture rather than buried in framework noise.

To turn capture down or off without touching `routes.json`:

```json
"Logging": {
  "LogLevel": {
    "ConduitSharp.Plugin.BodyCapture": "Warning"
  }
}
```

### Size the OTLP batch against your sink's message limit

Body capture makes every log record carry a multi-KB payload, so a batching exporter reaches its
sink's **maximum message size** far sooner than ordinary logging does. Get this wrong and capture
fails *silently*: the collector keeps accepting records, the gateway looks healthy, and nothing
reaches the log backend.

Do the arithmetic before enabling capture in production:

```
bytes per push  ≈  maxSize  ×  records per batch
```

With `maxSize: 4096` and an OpenTelemetry Collector `batch` processor left on its defaults
(`timeout` only, **no size cap**), a busy route accumulates a whole flush interval of records into
one push — measured on this repo's benchmark rig at **~15.9 MB**. Grafana Loki's gRPC receiver
caps a message at **4 MiB**, so every push was rejected:

```
HTTP 503, Message=rpc error: code = ResourceExhausted
desc = grpc: received message larger than max (15894276 vs. 4194304)
```

The exporter then retries **the same oversized batch** forever. It never shrinks, so it never
succeeds — only **3.4%** of captured bodies reached Loki, and the only trace of the problem was in
the collector's own log. Cap the batch on the sender, and raise the ceiling on the sink:

```yaml
# otel-collector.yaml — bound records per push, not just the flush interval
processors:
  batch:
    timeout: 5s
    send_batch_size: 500
    send_batch_max_size: 1000

# loki.yaml — headroom so a burst of larger bodies degrades instead of deadlocking
server:
  grpc_server_max_recv_msg_size: 16777216   # 16 MiB
  grpc_server_max_send_msg_size: 16777216
```

Both sides matter: the cap bounds the common case, the raised limit bounds the tail.

**To detect it**, compare what your sink *received* against what it *stored* — they diverge under
this failure. Watch for retries in the collector log (`Exporting failed. Will retry`); a steadily
climbing received-count with a flat stored-count is the same oversized batch being re-sent, not
traffic. Other sinks have their own ceiling (Kafka `message.max.bytes`, gRPC's own 4 MiB default);
the arithmetic above is what changes, not the failure mode.

## Security

Request bodies routinely carry credentials, tokens, and personal data. Both plugins log them verbatim — there is no redaction. Treat any route with body capture enabled as exporting its payloads to your log backend, and scope retention and access accordingly.

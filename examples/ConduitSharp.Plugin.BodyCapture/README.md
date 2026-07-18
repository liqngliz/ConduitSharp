# ConduitSharp BodyCapture Plugin

Plugins for the [ConduitSharp](https://github.com/liqngliz/ConduitSharp) API gateway that capture incoming HTTP request bodies and export them via structured OpenTelemetry logging — so you can ingest and search request bodies in downstream stacks like Grafana Loki.

Two variants ship here. **Prefer `body-capture-streaming`.**

| Variant | Buffers the body? | Works on `streamOnly`? | Use when |
| :--- | :--- | :--- | :--- |
| `body-capture-streaming` | No — logs a bounded prefix as the body streams past | Yes | Almost always |
| `body-capture` | Yes — forces the gateway to buffer the whole body | No | You need the *entire* body, at any size, and accept the cost |

## Installation

Drop the compiled plugin DLL into your gateway's configured `PluginsPath` (e.g. `./gateway/plugins`).

## `body-capture-streaming` (recommended)

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
      "variant": "body-capture-streaming",
      "order": 10,
      "enabled": true,
      "config": {
        "maxSize": 4096
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

| Setting | Type | Required | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| `maxSize` | `integer` | No | `4096` (4 KiB) | Bytes of the request body to log. Bodies longer than this are logged truncated. **Must not exceed 32768 (32 KiB)** — a larger value is rejected at startup. |

#### Why `maxSize` is capped at 32 KiB

Capture memory rides the *streaming* path, which never reserves against `Gateway:RequestLimits:MaxTotalBufferedBodyBytes` — so unlike a buffered body, nothing downstream sheds load if it grows. 32 KiB keeps each captured prefix well under the 85 KiB large object heap threshold (and matches HttpLogging's own `RequestBodyLogLimit` default), so capture stays on pooled, gen-0-sized buffers no matter how many requests are in flight.

If you need whole bodies, send them to a dedicated audit sink — don't raise this to route full payloads through your log pipeline.

### Behaviour inherited from HttpLogging

- **Only recognized media types are logged.** `application/json`, `text/*`, `application/xml`, and `application/*+json` are captured; `application/octet-stream` and other binary types stream through untouched and unlogged. Configure via HttpLogging's `MediaTypeOptions`.
- **Truncation is explicit.** An over-long body logs the prefix plus `RequestBodyStatus: [Truncated by RequestBodyLogLimit]`.
- **Requests with no `Content-Type` are not logged** (a `Debug`-level `No Content-Type header for request body` is emitted instead).

## `body-capture` (buffering)

Declares `ReadsRequestBody = true`, which forces the gateway to buffer the whole body into a rewindable stream — memory up to `Gateway:RequestLimits:MemoryBufferThresholdBytes`, then a temp-file spill — before the plugin runs. The gateway **rejects a `streamOnly` route carrying this plugin at startup.**

Enable with `"name": "body-capture"` (or `"name": "custom", "variant": "body-capture"`). Same `maxSize` option, uncapped, truncating with `... (truncated)`.

Reach for this only when a bounded prefix genuinely isn't enough. It puts every request body through the gateway's buffering budget, and a burst of large bodies sheds load with a 503.

## Observability

Both variants log at `Information` through the host's `ILoggerFactory`, so records flow out through whatever it has wired up — including the gateway's OpenTelemetry logger provider (→ OTLP → Loki), enriched with the ambient trace/route attributes.

| Variant | Category | Message |
| :--- | :--- | :--- |
| `body-capture-streaming` | `ConduitSharp.Plugin.BodyCapture.StreamingBodyCapturePlugin` | `Request and Response: RequestBody: …` / `RequestBodyStatus: …` |
| `body-capture` | `ConduitSharp.Plugin.BodyCapture.BodyCapturePlugin` | `Captured request body for path {Path}: {Body}` |

`body-capture-streaming` deliberately re-homes HttpLogging's loggers under its own category. HttpLogging natively logs under `Microsoft.AspNetCore.HttpLogging.*`, and every stock ASP.NET Core log config filters `Microsoft.AspNetCore` to `Warning` — which would drop every captured body silently while the plugin still looked healthy. Renaming means capture obeys the plugin's own log level, and the records land in Loki tagged as body-capture rather than buried in framework noise.

To turn capture down or off without touching `routes.json`:

```json
"Logging": {
  "LogLevel": {
    "ConduitSharp.Plugin.BodyCapture": "Warning"
  }
}
```

## Security

Request bodies routinely carry credentials, tokens, and personal data. Both plugins log them verbatim — there is no redaction. Treat any route with body capture enabled as exporting its payloads to your log backend, and scope retention and access accordingly.

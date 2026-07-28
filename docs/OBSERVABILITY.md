# Observability

_Part of the [ConduitSharp documentation](../README.md)._


ConduitSharp emits OpenTelemetry traces and metrics out of the box using `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter` — both named `"ConduitSharp.Gateway"`. There is zero overhead when no listener is attached.

### Traces

Every request produces a `gateway.request` span with the following tags:

| Tag | Example value |
| --- | --- |
| `http.request.method` | `GET` |
| `url.path` | `/api/users/42` |
| `conduitsharp.route_id` | `user-service-route` |
| `http.response.status_code` | `200` |

Spans are marked `Error` when the upstream returns 5xx. W3C `traceparent` headers are propagated automatically to upstream services via `AddHttpClientInstrumentation()`.

### Metrics

| Instrument | Unit | Description |
| --- | --- | --- |
| `conduitsharp.gateway.requests` | `{request}` | Total requests processed |
| `conduitsharp.gateway.request.duration` | `ms` | Request duration histogram |
| `conduitsharp.gateway.errors` | `{request}` | Requests that returned 5xx |

All instruments are tagged with `route_id`, `http.request.method`, and `http.response.status_code`.

### OTLP export

OTLP export is **disabled by default**. Enable it and set the endpoint in `Configuration/appsettings.json`:

```json
{
  "Gateway": {
    "Observability": {
      "Otlp": {
        "Enabled": true,
        "Endpoint": "http://localhost:4317"
      }
    }
  }
}
```

Or via environment variables:

```bash
Gateway__Observability__Otlp__Endpoint=http://localhost:4317
```

The standard `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable is also accepted as a fallback (read natively by the OTel SDK). Set `Gateway__Observability__Otlp__Enabled=false` to disable export entirely.

Any OTLP-compatible backend works: .NET Aspire dashboard, Jaeger, Grafana Tempo, Datadog, Honeycomb.

### Azure Monitor / Application Insights

The gateway emits standard OTLP, but **Azure Monitor is not a drop-in OTLP endpoint** you can point
`Otlp:Endpoint` straight at. Its native OTLP ingestion is in **preview** and needs three things a
plain OTLP exporter does not produce: Microsoft Entra token auth (scope
`https://monitor.azure.com/.default` + the *Monitoring Metrics Publisher* role on a Data Collection
Rule), per-signal `otlphttp` endpoints carrying a DCR immutable id, and **delta** metric temporality
(the exporter defaults to cumulative). So point the gateway at an **OpenTelemetry Collector** and let
it do the Azure-specific work — no gateway change:

```
gateway ──OTLP──▶ OTel Collector (azureauthextension) ──otlphttp + Entra──▶ Azure Monitor / App Insights
```

- **Gateway side:** the standard OTLP config above, `Endpoint` pointing at the collector. Nothing else.
- **Collector side:** the `azure_auth` extension (Collector ≥ 0.132; ≥ 0.148 for the current syntax)
  for Entra, the per-signal DCE endpoints, and `cumulativetodelta` on the metrics pipeline. If you
  would rather convert temporality at the source, set
  `OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE=delta` on the gateway instead — a pure env var,
  no code.
- **Captured request bodies ride this same pipeline** — they are `ILogger` records, so they flow to
  Azure Monitor's `traces` table (logs, not distributed-trace spans) as long as OTLP **logging**
  export is on (it is, when OTLP is enabled) and the capture category is not filtered out — see
  [Request body capture and log level](#request-body-capture-and-log-level) below.

**Embedding the library** (not the standalone host) has a second option: set
`ConfigureObservability = false` and let the host own OpenTelemetry, wiring the GA Azure Monitor distro
directly. The gateway takes no Azure dependency; it only exposes its `ActivitySource`/`Meter` names to
add to the host's providers:

```csharp
builder.AddConduitSharpGateway(o => o.ConfigureObservability = false);
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(GatewayTelemetry.SourceName, PipelineTelemetry.SourceName))
    .WithMetrics(m => m.AddMeter(GatewayTelemetry.SourceName))
    .UseAzureMonitor();   // wires all three signals INCLUDING logs — this is what carries captured bodies
```

`UseAzureMonitor()` matters here because captured bodies are **logs**: the tracing/metrics lines above
do not carry them. If you wire signals manually instead, you must also add the Azure Monitor **log**
exporter (`builder.Logging.AddOpenTelemetry(l => l.AddAzureMonitorLogExporter())`) or capture is
silently dropped.

### Horizontal scaling and state

By default, rate limiting and response caching are **in-memory and per-process**. Running multiple gateway instances behind a load balancer means:

- **Rate limits are per-instance by default — or shared via Valkey/Redis.** A client that hits two instances can make `maxRequests × instanceCount` requests per window before being throttled. Drop [`ConduitSharp.RateLimit.RedisProtocol`](../examples/ConduitSharp.RateLimit.RedisProtocol) into the gateway's plugins root and set the connection string: all instances then enforce one shared limit through the `IRateLimitStore` seam, failing open (allowing requests) if the backend is unreachable. No core changes required.
- **Cache is per-instance by default — or shared via Valkey/Redis.** Drop [`ConduitSharp.Cache.RedisProtocol`](../examples/ConduitSharp.Cache.RedisProtocol) into the gateway's plugins root and set `Gateway:Cache:Redis:ConnectionString`: all instances then share one distributed response cache over the Redis protocol (works with Valkey, Redis 7, or any RESP-compatible server), with request coalescing (stampede protection), route invalidation (`DELETE /admin/cache/{routeId}`), and fail-open behaviour if the cache is down. No core changes required.

Single-instance deployments (Windows Service, single container, IIS on one node) work out of the box with the in-memory cache and no external dependency.

### Request body capture and log level

The body-capture plugin emits captured bodies as **`ILogger` records at `Information`**, under the
category `ConduitSharp.Plugin.BodyCapture.*` — so they flow through whatever logging providers the
host has wired (OTLP → collector → Loki / Azure Monitor, console, file, …). The shipped
`appsettings.json` enables them explicitly:

```json
"Logging": { "LogLevel": {
  "Default": "Information",
  "Microsoft.AspNetCore": "Warning",
  "ConduitSharp.Plugin.BodyCapture": "Information"
}}
```

Two of these rules are a deliberate pair. ASP.NET Core's `HttpLogging` (which the streaming capture
path uses) natively logs under `Microsoft.AspNetCore.HttpLogging.*` — caught by the
`Microsoft.AspNetCore: Warning` rule and dropped. The plugin **re-homes** its records to the
`ConduitSharp.Plugin.BodyCapture.*` category to escape that filter, and the third rule pins that
category at `Information`. The third rule is redundant with `Default: Information` today, but it is a
deliberate anchor: a host that lowers `Default` to `Warning` (common in quiet-prod configs) still
gets capture, because this rule survives.

**When a request body is NOT captured** — by design or by config:

| cause | logged? | why |
|---|---|---|
| category filtered below `Information` (host sets `Default: Warning` and omits the `ConduitSharp.Plugin.BodyCapture` rule) | no | dropped before any exporter sees it — the one config trap |
| no logging exporter wired (embedded host with `ConfigureObservability=false` and only tracing/metrics) | no | captured bodies are logs; a traces/metrics-only pipeline never carries them |
| upstream unreachable / connection refused or dropped before the body was read | no | transport failure — the body is not its cause and not needed to diagnose it |
| rejected by an earlier plugin (401 / 403 auth, rate-limit) | no | the rejection is the cause, not the payload; in the shipped order (auth `order: 1`, capture `order: 10`) capture never runs for a rejected request |
| binary media type on a plain streaming route | no | `HttpLogging` only captures text-ish types; the reuse path (buffered/retry routes) captures binary too |

The last three are the plugin's **scope-to-cause** behaviour, not gaps: capture is limited to bodies
that were actually forwarded, so you are not storing payloads (PII, secrets, attacker input) for
failures the payload had no part in. Auditing rejected payloads on purpose is a deliberate,
narrower configuration — see the
[body-capture plugin README](../examples/ConduitSharp.Plugin.BodyCapture/README.md).

To turn capture down or off without touching `routes.json`, set the category to `Warning`:

```json
"Logging": { "LogLevel": { "ConduitSharp.Plugin.BodyCapture": "Warning" } }
```

### Structured logging

All gateway events (route matched, upstream error, plugin short-circuit) are emitted as structured JSON logs via `ILogger`.

`ASPNETCORE_ENVIRONMENT` controls which `appsettings.{Environment}.json` overlay is loaded and whether developer exception details are shown in responses. It is not set in the gateway config — set it in your host environment:

**Local development**

```bash
# PowerShell
$env:ASPNETCORE_ENVIRONMENT = "Development"
./ConduitSharp.Host.exe

# Command Prompt
set ASPNETCORE_ENVIRONMENT=Development
ConduitSharp.Host.exe

# bash / zsh
ASPNETCORE_ENVIRONMENT=Development ./ConduitSharp.Host
```

You can also create `Configuration/appsettings.Development.json` alongside the main file to override specific settings locally (e.g. point at a local upstream) without touching the production config.

**Linux / Docker**

```bash
# Inline
ASPNETCORE_ENVIRONMENT=Production ./ConduitSharp.Host

# Docker Compose
environment:
  - ASPNETCORE_ENVIRONMENT=Production
  - Gateway__AdminKeyHash=abc123...
```

**Windows Service (NSSM or sc.exe)**

Add `ASPNETCORE_ENVIRONMENT` as an environment variable in the service configuration. In NSSM this is under the *Environment* tab.

**IIS**

```xml
<aspNetCore processPath=".\ConduitSharp.Host.exe">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    <environmentVariable name="Gateway__AdminKeyHash" value="abc123..." />
  </environmentVariables>
</aspNetCore>
```

When not set, ASP.NET Core defaults to `Production`.

---


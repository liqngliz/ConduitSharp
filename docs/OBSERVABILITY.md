# Observability

_Part of the [ConduitSharp documentation](../README.md)._


OpenTelemetry traces and metrics out of the box via `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter`, both named `"ConduitSharp.Gateway"`. Zero overhead with no listener attached.

### Traces

Every request produces a `gateway.request` span:

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

`OTEL_EXPORTER_OTLP_ENDPOINT` is accepted as a fallback, read natively by the OTel SDK. `Gateway__Observability__Otlp__Enabled=false` disables export entirely.

Any OTLP-compatible backend works: .NET Aspire dashboard, Jaeger, Grafana Tempo, Datadog, Honeycomb.

### Azure Monitor / Application Insights

**Azure Monitor is not a drop-in OTLP endpoint.** Its native OTLP ingestion is in **preview** and
needs three things a plain OTLP exporter does not produce: Microsoft Entra token auth (scope
`https://monitor.azure.com/.default` plus the *Monitoring Metrics Publisher* role on a Data
Collection Rule), per-signal `otlphttp` endpoints carrying a DCR immutable id, and **delta** metric
temporality (the exporter defaults to cumulative). Point the gateway at an **OpenTelemetry
Collector** and let it do the Azure-specific work. No gateway change:

```
gateway ──OTLP──▶ OTel Collector (azureauthextension) ──otlphttp + Entra──▶ Azure Monitor / App Insights
```

- **Gateway side:** the standard OTLP config above, `Endpoint` pointing at the collector.
- **Collector side:** the `azure_auth` extension (Collector ≥ 0.132; ≥ 0.148 for the current syntax)
  for Entra, the per-signal DCE endpoints, and `cumulativetodelta` on the metrics pipeline. To
  convert temporality at the source instead, set
  `OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE=delta` on the gateway. Env var, no code.
- **Captured request bodies ride this same pipeline.** Being `ILogger` records, they land in Azure
  Monitor's `traces` table (logs, not distributed-trace spans) provided OTLP **logging** export is on
  (it is, when OTLP is enabled) and the capture category is not filtered out. See
  [Request body capture and log level](#request-body-capture-and-log-level).

**Embedding the library** (not the standalone host) has a second option: `ConfigureObservability =
false` leaves OpenTelemetry to the host, wiring the GA Azure Monitor distro directly. The gateway
takes no Azure dependency; it exposes its `ActivitySource`/`Meter` names for the host's providers:

```csharp
builder.AddConduitSharpGateway(o => o.ConfigureObservability = false);
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(GatewayTelemetry.SourceName, PipelineTelemetry.SourceName))
    .WithMetrics(m => m.AddMeter(GatewayTelemetry.SourceName))
    .UseAzureMonitor();   // all three signals INCLUDING logs, which is what carries captured bodies
```

Captured bodies are **logs**; the tracing/metrics lines above do not carry them. Wiring signals
manually means also adding the Azure Monitor **log** exporter
(`builder.Logging.AddOpenTelemetry(l => l.AddAzureMonitorLogExporter())`), or capture is silently
dropped.

### Horizontal scaling and state

Rate limiting and response caching are **in-memory and per-process** by default. Behind a load
balancer that means:

- **Rate limits are per-instance.** A client hitting two instances gets `maxRequests × instanceCount`
  requests per window. Drop [`ConduitSharp.RateLimit.RedisProtocol`](../plugins/ConduitSharp.RateLimit.RedisProtocol)
  into the gateway's plugins root and set the connection string: one shared limit across instances
  through the `IRateLimitStore` seam, failing open (allowing requests) if the backend is unreachable.
  No core changes.
- **Cache is per-instance.** Drop [`ConduitSharp.Cache.RedisProtocol`](../plugins/ConduitSharp.Cache.RedisProtocol)
  into the plugins root and set `Gateway:Cache:Redis:ConnectionString`: one distributed response
  cache over the Redis protocol (Valkey, Redis 7, any RESP-compatible server), with request
  coalescing (stampede protection), route invalidation (`DELETE /admin/cache/{routeId}`), and
  fail-open if the cache is down. No core changes.

Single-instance deployments (Windows Service, single container, IIS on one node) run on the
in-memory cache with no external dependency.

### Request body capture and log level

The body-capture plugin emits captured bodies as **`ILogger` records at `Information`**, category
`ConduitSharp.Plugin.BodyCapture.*`, so they flow through whatever logging providers the host has
wired (OTLP → collector → Loki / Azure Monitor, console, file). The shipped `appsettings.json`
enables them explicitly:

```json
"Logging": { "LogLevel": {
  "Default": "Information",
  "Microsoft.AspNetCore": "Warning",
  "ConduitSharp.Plugin.BodyCapture": "Information"
}}
```

Two of these rules are a deliberate pair. ASP.NET Core's `HttpLogging`, which the streaming capture
path uses, logs under `Microsoft.AspNetCore.HttpLogging.*`, caught by the `Microsoft.AspNetCore:
Warning` rule and dropped. The plugin **re-homes** its records to `ConduitSharp.Plugin.BodyCapture.*`
to escape that filter, and the third rule pins that category at `Information`. Redundant with
`Default: Information` today, it is an anchor: a host that lowers `Default` to `Warning` (common in
quiet-prod configs) still gets capture.

**When a request body is NOT captured**, by design or by config:

| cause | logged? | why |
|---|---|---|
| category filtered below `Information` (host sets `Default: Warning` and omits the `ConduitSharp.Plugin.BodyCapture` rule) | no | dropped before any exporter sees it, the one config trap |
| no logging exporter wired (embedded host, `ConfigureObservability=false`, tracing/metrics only) | no | captured bodies are logs; a traces/metrics-only pipeline never carries them |
| upstream unreachable, connection refused or dropped before the body was read | no | transport failure; the body is neither its cause nor needed to diagnose it |
| rejected by an earlier plugin (401 / 403 auth, rate-limit) | no | in the shipped order (auth `order: 1`, capture `order: 10`) capture never runs for a rejected request |
| binary media type on a plain streaming route | no | `HttpLogging` captures text-ish types only; the reuse path (buffered/retry routes) captures binary too |

The last three are the plugin's **scope-to-cause** behaviour. Capture is limited to bodies actually
forwarded, so payloads (PII, secrets, attacker input) are not stored for failures the payload had no
part in. Auditing rejected payloads is a deliberate, narrower configuration; see the
[body-capture plugin README](../plugins/ConduitSharp.Plugin.BodyCapture/README.md).

Turning capture down or off without touching `routes.json`:

```json
"Logging": { "LogLevel": { "ConduitSharp.Plugin.BodyCapture": "Warning" } }
```

### Structured logging

All gateway events (route matched, upstream error, plugin short-circuit) are structured JSON logs via `ILogger`.

`ASPNETCORE_ENVIRONMENT` controls which `appsettings.{Environment}.json` overlay loads and whether developer exception details appear in responses. Not set in the gateway config; set it in the host environment.

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

`Configuration/appsettings.Development.json` alongside the main file overrides settings locally (e.g. point at a local upstream) without touching the production config.

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


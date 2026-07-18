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

### Horizontal scaling and state

By default, rate limiting and response caching are **in-memory and per-process**. Running multiple gateway instances behind a load balancer means:

- **Rate limits are per-instance by default — or shared via Valkey/Redis.** A client that hits two instances can make `maxRequests × instanceCount` requests per window before being throttled. Drop [`ConduitSharp.RateLimit.RedisProtocol`](../examples/ConduitSharp.RateLimit.RedisProtocol) into the gateway's plugins root and set the connection string: all instances then enforce one shared limit through the `IRateLimitStore` seam, failing open (allowing requests) if the backend is unreachable. No core changes required.
- **Cache is per-instance by default — or shared via Valkey/Redis.** Drop [`ConduitSharp.Cache.RedisProtocol`](../examples/ConduitSharp.Cache.RedisProtocol) into the gateway's plugins root and set `Gateway:Cache:Redis:ConnectionString`: all instances then share one distributed response cache over the Redis protocol (works with Valkey, Redis 7, or any RESP-compatible server), with request coalescing (stampede protection), route invalidation (`DELETE /admin/cache/{routeId}`), and fail-open behaviour if the cache is down. No core changes required.

Single-instance deployments (Windows Service, single container, IIS on one node) work out of the box with the in-memory cache and no external dependency.

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


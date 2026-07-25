# LegacyGateway — end-to-end example

Runs the routes from the README "At a glance" diagram as working services.

```
/api/inventory/{**rest}   → api-key-auth + rate-limit + http-proxy (RoundRobin 5101/5102)
/api/orders/{**rest}      → jwt-auth + http-proxy (5201)
/erp/reports/summary   → jwt-auth + rate-limit + cache + power-shell (PS script, no upstream)
/health                   → http-proxy passthrough (5101)
```

## Prerequisites

- [Docker](https://www.docker.com/) for the `make docker-up` / `make docker-grafana` quick starts
- [.NET 10 SDK](https://dotnet.microsoft.com/download) and [PowerShell 7](https://aka.ms/pscore6) (`pwsh`) for the local-process launcher scripts

## Quick start

**Docker + Aspire Dashboard (recommended)**

```sh
cd examples/LegacyGateway
make docker-up            # macOS / Linux
pwsh start.ps1 -DockerUp  # Windows
```

Gateway at `http://localhost:5050`, traces/metrics/logs at `http://localhost:18888`. Stop with `make docker-down` (macOS/Linux) or `pwsh start.ps1 -DockerDown` (Windows).

**Docker + Grafana (Tempo/Prometheus/Loki)**

```sh
cd examples/LegacyGateway
make docker-grafana                                            # macOS / Linux
docker compose -f docker-compose.grafana.yml up --build -d      # Windows
```

Gateway at `http://localhost:5850`, Grafana at `http://localhost:3000` (no login). Stop with `make docker-grafana-down` (macOS/Linux) or `docker compose -f docker-compose.grafana.yml down -v` (Windows).

**Local processes, no Docker (macOS / Linux, GNU make)**

```sh
cd examples/LegacyGateway
make run
```

**Local processes, no Docker (Windows, PowerShell 7)**

```sh
cd examples/LegacyGateway
pwsh start.ps1
```

Or double-click **`start.bat`** (Windows).

## Try the routes

```sh
# Health — no auth
curl http://localhost:5050/health

# Inventory — API key
curl http://localhost:5050/api/inventory \
     -H "X-Api-Key: demo-api-key-conduitsharp-example"

curl http://localhost:5050/api/inventory/1 \
     -H "X-Api-Key: demo-api-key-conduitsharp-example"

# Orders + ERP — JWT (generate a 60-minute demo token first)
TOKEN=$(pwsh generate-token.ps1)

curl http://localhost:5050/api/orders \
     -H "Authorization: Bearer $TOKEN"

curl http://localhost:5050/api/orders/ORD-002 \
     -H "Authorization: Bearer $TOKEN"

# ERP report — JWT + cache (60s TTL) + PowerShell ERP script
curl http://localhost:5050/erp/reports/summary \
     -H "Authorization: Bearer $TOKEN"
```

## Stop

```sh
make docker-down                                            # Docker + Aspire, macOS / Linux
pwsh start.ps1 -DockerDown                                  # Docker + Aspire, Windows
make docker-grafana-down                                    # Docker + Grafana, macOS / Linux
docker compose -f docker-compose.grafana.yml down -v        # Docker + Grafana, Windows
make stop                                                    # local processes, macOS / Linux
pwsh start.ps1 -Stop                                         # local processes, Windows
```

## Project layout

```
../SharedServices/
  InventoryService/   ASP.NET minimal API, runs on :5101 and :5102
  OrderService/       ASP.NET minimal API, runs on :5201
plugins/
  PowerShellPlugin/   IPipelinePlugin drop-in that executes .ps1 scripts
gateway/
  Configuration/
    routes.json       Four-route pipeline configuration
    appsettings.json  Kestrel + logging settings
  scripts/
    Get-ErpReport.ps1   Demo ERP value report (mock data)
  plugins/            Built plugin DLLs are copied here by the launcher
logs/                 One log file per service + gateway (created at runtime)
generate-token.ps1    Generates a signed demo JWT matching routes.json
Makefile              Linux / macOS launcher (make run / stop / clean)
start.ps1             Cross-platform launcher (pwsh)
start.bat             Windows double-click wrapper for start.ps1
```

## How the PowerShell plugin works

The `PowerShellPlugin` is built as a class library and dropped into `gateway/plugins/`.
ConduitSharp scans that directory at startup and registers any `IPipelinePlugin` it finds.
The erp route never reaches an upstream — the PS plugin short-circuits with a 200 response
after running the script.

Set `Gateway__PluginsPath` to point to any directory with plugin DLLs.

## JWT details

Both the `order-service` and `erp-report` routes use HS256 with:

| Field      | Value                                        |
|------------|----------------------------------------------|
| signingKey | `demo-signing-key-conduitsharp-example-32ch` |
| issuer     | `conduitsharp-demo`                          |
| audience   | `conduitsharp-demo`                          |

**Never use these credentials in production.** Replace with a strong random key and
a proper issuer in your own `routes.json`.

The `erp-report` route additionally requires a `role` claim of `analyst` or
`erp-admin` (`jwt-auth`'s `requiredClaims` — see the main [README](../../docs/AUTHORIZATION.md#claim-based-authorization-rbac)).
The demo token from `generate-token.sh`/`.ps1` already carries `"role": "analyst"`, so the
quick-start `curl` command above works unchanged. A structurally valid token missing that
role gets `403 Forbidden`, not `401` — the token itself is fine, the caller just lacks
permission for this route.

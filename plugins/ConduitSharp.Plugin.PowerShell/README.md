# ConduitSharp PowerShell Plugin

A ready-to-use plugin for the [ConduitSharp](https://github.com/liqngliz/ConduitSharp) API gateway that runs an existing PowerShell (`.ps1`) script in-process as an HTTP endpoint.

The PowerShell 7 runtime is embedded via `Microsoft.PowerShell.SDK` — no system `pwsh` installation is required on the gateway host. The script produces the HTTP response directly, so legacy Windows automation becomes an authenticated, rate-limited, observable API without rewriting a line of it.

## Installation

Drop the published plugin output (the plugin DLL **and** its `Microsoft.PowerShell.SDK` dependencies) into your gateway's configured `PluginsPath` (e.g., `./gateway/plugins/<route-id>/`), or reference the `ConduitSharp.Plugin.PowerShell` package and register it in DI when embedding the gateway.

## Configuration

Enable the plugin on any route in your `routes.json` by adding `"name": "custom", "variant": "power-shell"` to the route's plugin list. Use it as the terminal plugin (no `http-proxy` after it) — the script writes the response.

### Example `routes.json`

```json
{
  "id": "erp-report",
  "route": {
    "match": { "path": "/erp/reports/summary", "methods": ["GET"] }
  },
  "cluster": null,
  "plugins": [
    { "name": "jwt-auth",   "order": 1, "config": { "signingKey": "..." } },
    { "name": "rate-limit", "order": 2, "config": { "windowSeconds": 3600, "maxRequests": 100 } },
    { "name": "custom", "variant": "power-shell", "order": 99, "config": { "scriptPath": "scripts/Get-ErpReport.ps1" } }
  ]
}
```

### Options

| Setting | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `scriptPath` | `string` | Yes | Path to the `.ps1` to run. Relative paths resolve against the gateway's base path. |

The script receives the current request as a `$Request` parameter. `$ErrorActionPreference = 'Stop'` is applied, so both terminating and non-terminating script errors surface as a `500` rather than an empty body.

## Production notes

Each request currently creates a fresh PowerShell instance. For concurrent load or heavy ETL workloads see [PowerShell plugin — production considerations](../../docs/ARCHITECTURE.md#powershell-plugin--production-considerations) (runspace pooling, out-of-process execution, PSCustomObject memory guidance).

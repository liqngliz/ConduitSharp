# src/ConduitSharp.Host/Program.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## (file)

- (was line 6) Configuration — load Gateway settings from Configuration/appsettings.json. This file-layout convention is specific to the standalone host package; the AddConduitSharpGateway() library only binds whatever Gateway section it finds on builder.Configuration. All values can be overridden via environment variables: Gateway__AdminKeyHash=secret Gateway__RoutesPath=/etc/conduit/routes.json Gateway__Observability__Otlp__Enabled=true
- (was line 14) Config priority (highest → lowest): 1. Environment variables 2. GATEWAY_CONFIG_FILE overlay  (e.g. configuration-vm/appsettings.json) 3. Configuration/appsettings.json  (base defaults)
- (was line 19) AddEnvironmentVariables() is re-added AFTER the JSON files so env vars always win (the default pipeline adds env vars before these files, so priority order would be wrong without the explicit re-add here).
- (was line 34) Wire the gateway. Defaults reproduce the full standalone host: OTel exporters, per-route plugin-folder scanning, admin API, health endpoints, and Swagger UI.
- (was line 40) Aggregated Swagger UI (optional add-on) — before the terminal gateway middleware.

## Program

- (was line 47) Required for WebApplicationFactory<Program> in integration tests.

using ConduitSharp.Gateway;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration — load Gateway settings from Configuration/appsettings.json.
// This file-layout convention is specific to the standalone host package; the
// AddConduitSharpGateway() library only binds whatever Gateway section it finds
// on builder.Configuration. All values can be overridden via environment variables:
//   Gateway__AdminKeyHash=secret
//   Gateway__RoutesPath=/etc/conduit/routes.json
//   Gateway__Observability__Otlp__Enabled=true
//
// Config priority (highest → lowest):
//   1. Environment variables
//   2. GATEWAY_CONFIG_FILE overlay  (e.g. configuration-vm/appsettings.json)
//   3. Configuration/appsettings.json  (base defaults)
//
// AddEnvironmentVariables() is re-added AFTER the JSON files so env vars always win
// (the default pipeline adds env vars before these files, so priority order would be
// wrong without the explicit re-add here).
// ---------------------------------------------------------------------------
builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "Configuration", "appsettings.json"),
    optional: true,
    reloadOnChange: false);

var configOverlay = Environment.GetEnvironmentVariable("GATEWAY_CONFIG_FILE");
if (!string.IsNullOrEmpty(configOverlay))
    builder.Configuration.AddJsonFile(configOverlay, optional: false, reloadOnChange: false);

builder.Configuration.AddEnvironmentVariables();

// Wire the gateway. Defaults reproduce the full standalone host: OTel exporters,
// per-route plugin-folder scanning, admin API, health endpoints, and Swagger UI.
builder.AddConduitSharpGateway();

var app = builder.Build();

// Aggregated Swagger UI (optional add-on) — before the terminal gateway middleware.
app.UseConduitSharpGatewaySwagger();

app.UseConduitSharpGateway();

app.Run();

// Required for WebApplicationFactory<Program> in integration tests.
public partial class Program { }

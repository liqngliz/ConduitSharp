# Contributing to ConduitSharp

## Requirements

- **.NET 10 SDK** — all projects target `net10.0`.
- **PowerShell 7** (`pwsh`) — required to run the LegacyGateway example launcher and its PowerShell plugin.
- **Docker** — required for `ConduitSharp.Grafana.E2E.Tests` and `ConduitSharp.Mtls.E2E.Tests`, and for the LegacyGateway `make docker-up` stack. Unit and integration tests (`ConduitSharp.*.Tests`, `ConduitSharp.Integration.Tests`) don't need it.
- **GNU make** — used by the repo's `Makefile` targets (`make test`, `make coverage`) and the LegacyGateway example; optional if you run the underlying `dotnet` commands directly.

## Build from source

```bash
git clone https://github.com/liqngliz/ConduitSharp
cd ConduitSharp
dotnet run --project src/ConduitSharp.Host
```

## Running tests

```bash
dotnet test                  # run all tests
dotnet test --filter "Unit"  # unit tests only
dotnet test --filter "Integration"  # integration tests only
```

Via make:

```bash
make test        # run all tests
make coverage    # run tests + open HTML coverage report
make clean       # remove TestResults/
```

## Project structure

```
src/
  ConduitSharp.Core/            # Plugin contracts, route models, pipeline executor — published as NuGet
  ConduitSharp.Host/            # Kestrel entry point, DI wiring, plugin discovery, Swagger aggregation
  ConduitSharp.Security/        # jwt-auth, jwks-jwt-auth, api-key-auth, api-key-auth-hashed
  ConduitSharp.Traffic/         # rate-limit, cache
  ConduitSharp.Transformation/  # header-transform
  ConduitSharp.Observability/   # structured logging, OTel metrics, GatewayTelemetry
tests/
  ConduitSharp.Core.Tests/           # Unit — routing, pipeline, deserialization
  ConduitSharp.Security.Tests/       # Unit — all security plugins and handlers
  ConduitSharp.Traffic.Tests/        # Unit — traffic plugins (rate-limit, cache)
  ConduitSharp.Transformation.Tests/ # Unit — header-transform plugin
  ConduitSharp.Observability.Tests/  # Unit — logging and metrics
  ConduitSharp.Integration.Tests/    # End-to-end via WebApplicationFactory + FakeUpstream
examples/
```

**Dependency rule:** feature packages (`Security`, `Traffic`, `Transformation`, `Observability`) reference only `Core` — never each other or `Host`. `Host` is the single aggregation point.

## Adding a new built-in plugin

1. Add a value to `PluginName` in `src/ConduitSharp.Core/Routing/RoutingEnums.cs`.
2. Create the plugin class in the appropriate feature package, implementing `IPipelinePlugin`.
3. Register it in `src/ConduitSharp.Host/Program.cs` via `builder.Services.AddSingleton<IPipelinePlugin, MyPlugin>()`.
4. Add XML doc with `<summary>`, `<example>`, and per-property comments to the config record — see existing plugins for the pattern.
5. Add unit tests in the corresponding test project.

Note: the `PluginName` enum is closed and validated at startup via `StrictEnumConverter<T>`. External plugin authors cannot add new names without a source change — they use `PluginName.Custom` or `PluginName.PowerShell` for terminal handlers.

## Integration test patterns

Tests use `WebApplicationFactory<Program>` via `GatewayFactory` and a real in-process HTTP server (`FakeUpstream`) as the upstream stand-in.

```csharp
// Standard setup
_upstream = await FakeUpstream.StartAsync();
_factory  = await GatewayFactory.CreateAsync(_upstream);
_client   = _factory.CreateClient();

// Control upstream responses
_upstream.RespondWith(503);
_upstream.RespondWith(async ctx => { ctx.Response.StatusCode = 200; });

// Custom routes JSON
await using var factory = await GatewayFactory.CreateAsync(_upstream, routesJson);

// Inject test plugins
await using var factory = await GatewayFactory.CreateAsync(
    _upstream, routesJson, plugins: [new MyTestPlugin()]);

// Override HttpClient timeout (to exercise 504 path)
await using var factory = await GatewayFactory.CreateAsync(
    _upstream, upstreamHttpTimeout: TimeSpan.FromMilliseconds(150));
```

`Gateway__RoutesPath` env var is set by `GatewayFactory.CreateAsync` to a temp file and cleared on dispose. `xunit.runner.json` disables parallel test collection to prevent race conditions when multiple test classes write this process-level env var concurrently.

### Testing OTel tracing branches

Register an `ActivityListener` before creating the client to make `StartActivity()` return non-null:

```csharp
using var listener = new ActivityListener
{
    ShouldListenTo  = source => source.Name == GatewayTelemetry.SourceName,
    Sample          = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStarted = _ => { },
    ActivityStopped = a => spans.Add(a),
};
ActivitySource.AddActivityListener(listener);
```

## Releasing

Releases are triggered by pushing a `v*` tag. The `release.yml` workflow produces:

- `conduitsharp-vX.X.X-win-x64.zip` — self-contained single-file Windows binary
- `conduitsharp-vX.X.X-linux-x64.tar.gz` — self-contained single-file Linux binary
- Docker image pushed to GHCR with semver + `latest` tags
- `ConduitSharp.Gateway` NuGet tool package published via keyless OIDC (no stored API key)

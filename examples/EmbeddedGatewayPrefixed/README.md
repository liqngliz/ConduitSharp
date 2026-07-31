# EmbeddedGatewayPrefixed — embedding ConduitSharp with a Path Prefix

Demonstrates the **fourth distribution method**: instead of running ConduitSharp as a
standalone process (`make run`), a `dotnet tool`, or a Docker image, you reference the
[`ConduitSharp.Gateway.AspNetCore`](../../src/ConduitSharp.Gateway.AspNetCore) NuGet package
and host the gateway *inside your own ASP.NET Core app* — the YARP
`AddReverseProxy()` / `MapReverseProxy()` model.

## The whole integration

```csharp
builder.AddConduitSharpGateway(options =>
{
    options.EnablePluginDirectoryScan = false;
    options.PathPrefix = "/api";        // gateway owns /api/*; the rest is yours
});

var app = builder.Build();
app.MapGet("/hello", () => "served by the host app");   // coexists with the gateway
app.UseConduitSharpGateway();
app.Run();
```

Two calls: `AddConduitSharpGateway` (services) and `UseConduitSharpGateway` (middleware).

## Composition knobs (`ConduitSharpGatewayOptions`)

The defaults reproduce the full standalone host. When embedding, turn off what the host app
already owns:

| Option | Default | Turn off when… |
| --- | --- | --- |
| `ConfigureObservability` | `true` | the host app already calls `AddOpenTelemetry()` |
| `EnablePluginDirectoryScan` | `true` | you register plugins in DI, not via a `plugins/` folder |
| `EnableAdminApi` | `true` | a reload restarting the whole process is unacceptable |
| `MapHealthEndpoints` | `true` | the host app owns `/healthz` and `/readyz` |
| `PathPrefix` | `null` (root) | the gateway should own only a sub-path |
| `Routes` / `RoutesPath` | `Gateway:RoutesPath` | you supply routes in code or a custom path |

Aggregated **Swagger UI** is an optional add-on so embedders don't take a Swashbuckle
dependency they don't want: add the `ConduitSharp.Gateway.AspNetCore.Swagger` package and call
`app.UseConduitSharpGatewaySwagger()` **before** `app.UseConduitSharpGateway()`.

## Adding a plugin from NuGet — in code

This sample also swaps a pluggable service for a package that ships on its own, entirely in code
(no `plugins/` folder). `AddConduitSharpGateway` builds the pipeline from *all* registered
`IPipelinePlugin` / `ICacheService` services, so anything registered after it is picked up
automatically, and re-registering a key replaces the built-in:

// Register custom plugins that aren't built-in
builder.Services.AddSingleton<IPipelinePlugin, BodyCapturePlugin>();
builder.Services.AddSingleton<IPipelinePlugin, PowerShellPlugin>();

A plugin whose `Id` is `"cache"`/`"rate-limit"`/etc. must be **declared in the route's plugin
list** to run in-chain. A custom third-party plugin uses `PluginName.Custom` + a `variant` instead
— its `Id` is what the registry actually keys on, so the numeric `PluginName` enum can shift
across `ConduitSharp.Core` releases without breaking the plugin.

### Composition note: caching works because the forward runs inside the chain

Forwarding is YARP's `IHttpForwarder`, and `http-proxy` in the route's plugin list just names
**where in the chain** the forward happens. The chain's terminal step calls into the rest of YARP's
proxy pipeline, so the forwarder always executes *within* the plugins' `next()`.

That ordering is what makes caching work with no cooperation from the proxy: the cache plugin swaps
`Response.Body` for a tee stream before calling `next()`, and the forwarder streams through it. No
`DelegatingHandler`, no capture callback, no engine-specific bridge — and concurrent misses for the
same key coalesce onto one upstream fetch.

## Run it

```bash
dotnet run --project examples/EmbeddedGatewayPrefixed
# GET /                    -> handled by the host app ("HI")
# GET /api/health          -> forwarded upstream by YARP, mapped to /health
# GET /api/swagger         -> Swagger UI correctly mapped under /api
curl -i localhost:7050/api/health
```

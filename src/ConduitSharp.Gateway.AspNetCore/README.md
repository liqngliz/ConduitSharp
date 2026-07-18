# ConduitSharp.Gateway.AspNetCore

Embed the ConduitSharp API gateway inside your own ASP.NET Core application.

## Usage

Add to your ASP.NET Core host:

```csharp
builder.Services.AddConduitSharpGateway(opts =>
{
    opts.ConfigPath = "conduit-routes.json";
    opts.PluginPath = "./plugins";
});

var app = builder.Build();
app.MapConduitSharpGateway();
app.Run();
```

Routes are defined in JSON:

```json
{
  "routes": [
    {
      "path": "/api/users",
      "methods": ["GET"],
      "upstream": "http://backend:3000",
      "plugins": ["my-plugin", "auth"]
    }
  ]
}
```

## Plugin System

Plugins run per-route to inspect, transform, or filter requests/responses. Drop a compiled plugin assembly into your `plugins/` folder and reference it in route config — no restart required.

Build plugins with [ConduitSharp.Core](https://www.nuget.org/packages/ConduitSharp.Core).

## Drop-in Plugin Packages

The NuGet ecosystem provides ready-to-use plugins:

- [ConduitSharp.Plugin.YarpProxy](https://www.nuget.org/packages/ConduitSharp.Plugin.YarpProxy) — HTTP/2, WebSocket, streaming
- [ConduitSharp.Cache.RedisProtocol](https://www.nuget.org/packages/ConduitSharp.Cache.RedisProtocol) — Redis-backed response cache
- [ConduitSharp.RateLimit.RedisProtocol](https://www.nuget.org/packages/ConduitSharp.RateLimit.RedisProtocol) — Redis-backed rate limiter

## Observability

Structured logging, distributed traces (OpenTelemetry), and Prometheus metrics are built in. Wire them into your observability stack:

```csharp
builder.Services
    .AddLogging(l => l.AddOpenTelemetry())
    .AddOpenTelemetry()
    .WithTracing(t => t.AddConduitSharpInstrumentation());
```

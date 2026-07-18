# ConduitSharp.Gateway.AspNetCore.Swagger

OpenAPI (Swagger) schema generation and UI for ConduitSharp routes.

Automatically generates OpenAPI documentation from your route config and serves Swagger UI:

```csharp
app.UseConduitSharpGatewaySwagger(); // Should be called before app.UseConduitSharpGateway()
```

Upstream service schemas are aggregated, merged, and served at a single endpoint so clients see a unified API contract.
It automatically honors `PathPrefix` defined in `ConduitSharpGatewayOptions` so your swagger UI and spec files can safely live under a prefix (like `/api/swagger`).

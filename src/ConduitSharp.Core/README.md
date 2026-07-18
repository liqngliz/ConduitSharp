# ConduitSharp.Core

Plugin contracts and routing primitives for ConduitSharp.

## For Plugin Developers

Reference this package to build plugins that run inside the ConduitSharp gateway.

```csharp
using ConduitSharp.Core.Plugins;

public class MyPlugin : IPlugin
{
    public string Name => "my-plugin";
    public async Task<PluginResult> ExecuteAsync(PluginContext ctx)
    {
        // Inspect the incoming request
        var method = ctx.HttpContext.Request.Method;
        var path = ctx.HttpContext.Request.Path;
        
        // Call the next handler in the chain
        return await ctx.NextAsync();
    }
}
```

Drop the compiled assembly into the gateway's `plugins/` folder — no gateway rebuild required.

## Included Types

- `IPlugin` — plugin interface
- `IRouteMatcher` — route selection logic (swap the default matcher)
- `IRateLimitStore` — distributed rate-limit backend
- `ICacheService` — distributed response cache backend
- `IRequestHandler`, `ITransformPlugin` — specialized plugin bases
- Request/response models and pipeline context

## See Also

- [ConduitSharp.Gateway.AspNetCore](https://www.nuget.org/packages/ConduitSharp.Gateway.AspNetCore) — embed the gateway in your app
- Examples: [ConduitSharp.Plugin.YarpProxy](https://www.nuget.org/packages/ConduitSharp.Plugin.YarpProxy), [ConduitSharp.Cache.RedisProtocol](https://www.nuget.org/packages/ConduitSharp.Cache.RedisProtocol)

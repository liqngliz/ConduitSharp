# ConduitSharp — Agent Reference

## What this project is

ConduitSharp is an API gateway built on ASP.NET Core / .NET 10. It receives HTTP
requests, matches them to configured routes, runs an ordered plugin pipeline, then
proxies the request to an upstream service and streams the response back. Routes and
plugins are configured via a `routes.json` file; no code changes are needed to add
or modify routes.

The gateway logic itself lives in **`ConduitSharp.Gateway.AspNetCore`**, an embeddable
library (`AddConduitSharpGateway` / `UseConduitSharpGateway` — the same shape as YARP's
`AddReverseProxy()` / `MapReverseProxy()`). **`ConduitSharp.Host`** is a ~40-line
executable shell over that library — it is what ships as the `conduitsharp` dotnet tool
and the Docker image. Aggregated Swagger UI is a separate opt-in add-on package
(`ConduitSharp.Gateway.AspNetCore.Swagger`) so embedders who don't want it avoid the
Swashbuckle dependency.

---

## Request lifecycle (end-to-end)

```
HTTP request
  → Kestrel
  → Admin middleware (Use()) — POST /admin/routes/reload, DELETE /admin/cache/{routeId}
                               active only when Gateway:AdminKeyHash is set    [EnableAdminApi]
  → Health middleware (Use()) — /healthz (liveness), /readyz (readiness)      [MapHealthEndpoints]
  → SwaggerUI + spec proxy middleware — /swagger/**   [optional add-on: app.UseConduitSharpGatewaySwagger()]
  → OTel ActivitySource.StartActivity("gateway.request")   [every gateway request, including
                                                           ones that match no route]
  → ASP.NET Core endpoint routing   native DFA over path + method + header + query
                                    (route list order → RouteConfig.Order, so declaration order
                                     breaks overlaps: first match wins)
      → 404 if no route matched, 405 if the path matched but the verb did not
        (both before a single byte of the body is read)

  ── route HAS an upstream → YARP proxy pipeline (app.MapReverseProxy) ──
      → plugin dispatch    resolves the route's precompiled RequestDelegate by RouteId
                           (GatewayRouteTable.ChainFor) and sets:
                             Items["ConduitSharp.RouteId"]    the route id (a string)
                             Items["ConduitSharp.ProxyNext"]  continuation into YARP's pipeline
          → BufferRequestBody   ONLY when the route has a consumer for the buffer: a retry policy
                                (and the method is idempotent — a POST on a retry route streams) or a
                                body-reading plugin. All other requests stream (same as streamOnly).
                                Buffered: route.MaxRequestBodyBytes (else Gateway:RequestLimits
                                .MaxRequestBodyBytes) → 413. Two tiers, via RequestBodyBudget:
                                RAM while MaxMemoryBufferedBodyBytes (default 64 MiB) has headroom
                                (≤ MemoryBufferThresholdBytes, default 1 MiB, per body); disk spill
                                once it doesn't (FileBufferingReadStream temp file, SpillDirectory);
                                503 only when MaxTotalBufferedBodyBytes (RAM + spill) is exhausted.
                                Leaves a seekable body so plugins can inspect it and the retry loop
                                can rewind it.
          → ordered plugin chain (jwt-auth → rate-limit → cache → header-transform → …)
              → a plugin that writes a response and does not call next() short-circuits;
                YARP never forwards
              → chain's terminal step invokes ProxyNext — so the FORWARD RUNS INSIDE the
                plugins' next(). This is why the cache plugin's tee wraps the real forward
                and coalescing always engages.
      → UpstreamProtocol      inbound HTTP/2 → h2c prior-knowledge cluster model (keeps gRPC alive)
      → UpstreamRetry         Polly ResiliencePipeline per route; idempotent methods only
                              (GET/HEAD/OPTIONS/PUT/DELETE/TRACE — a POST/PATCH never retries).
                              Per attempt: rewind body → restore the full destination set →
                              next() → judge outcome. A retryable attempt's response is
                              suppressed (SuppressRetriedResponseTransform) so it never reaches
                              the client, and its status/headers are reset before the next try.
      → UseLoadBalancing      re-picks a destination each attempt → cross-node failover
      → UsePassiveHealthChecks  ConsecutiveFailuresHealthPolicy: circuitBreaker.threshold
                              consecutive failures → destination unhealthy for
                              circuitBreaker.cooldownMs. Client disconnect never counts.
      → ForwarderMiddleware   IHttpForwarder.SendAsync — streams headers, body, trailers.
                              504 on timeout (ActivityTimeout = route timeoutMs, per attempt),
                              502 on transport failure.

  ── route has NO cluster ("cluster": null) → plain endpoint, YARP never sees it ──
      → same precompiled chain; terminal step is 502 if no plugin produced a response

  → finally: release the RequestBodyBudget reservation; tag the activity with the status code,
    mark Error if 5xx; notify IRequestObserver list (RequestObservation)
```

---

## Plugin system

### Contract

```csharp
public interface IPipelinePlugin
{
    PluginName Name { get; }
    string? Variant => null;                                    // disambiguator for PluginName.Custom
    Task ExecuteAsync(PluginContext context, PluginDelegate next);
    void ValidateConfig(JsonElement config) { }                  // startup/reload fail-fast; default no-op
}
```

`PluginDelegate` is `Task(PluginContext)` — the rest of the chain. The middleware
pattern applies: do work, call `next`, optionally do more work after `next` returns.
To block a request call `context.ShortCircuit(statusCode, body?)` and **return
without calling next**.

### Two separate validation moments

- **`ValidateConfig(JsonElement)`** — called once per enabled route/plugin at startup
  and on admin reload, via `PluginPipelineExecutor.ValidateRouteConfigs`. Throw here to
  fail fast on structurally-valid-but-semantically-wrong config (e.g. `rate-limit` with
  `maxRequests <= 0`, `cache` with a non-positive `ttlSeconds`, `jwks-jwt-auth` missing
  `jwksUri`). Default implementation is a no-op — most plugins skip it and rely on the
  pattern below.
- **`static From(JsonElement)` factory** — the config record's own deserialize+validate
  entry point, called by convention at the top of `ExecuteAsync` on every request:

```csharp
public Task ExecuteAsync(PluginContext context, PluginDelegate next)
{
    var config = MyConfig.From(context.PluginConfig); // deserialize + validate here
    // ... plugin logic with typed config
}
```

Implementing `ValidateConfig` (even as `configLoader(config)` reusing the same `From`
factory, as `JwksJwtAuthPlugin` does) moves the failure to startup instead of first
request; it is optional but recommended for anything with a "this must be positive" invariant.

### PluginContext fields available to a plugin

| Member | Type | Notes |
|---|---|---|
| `Request` | `GatewayRequest` | Snapshot of the incoming request including seekable body stream |
| `RouteMatch` | `RouteMatchResult` | The matched route + captured path params |
| `PluginConfig` | `JsonElement` | This plugin's `config` block from routes.json. **Save to a local before calling next** — the executor overwrites it for the next plugin |
| `ShortCircuitHeaders` | `Dictionary<string,string>` | Add response headers here before calling `ShortCircuit` |
| `IsShortCircuited` | `bool` | Read-only; true after any plugin calls ShortCircuit |
| `ResponseCaptureCallback` | `Func<int, string?, string, Task>?` | Set before calling `next` to receive `(statusCode, contentType, body)` after the upstream responds (2xx only, subject to `ResponseCaptureLimitBytes`). Used by `CachePlugin`. |
| `ResponseCaptureLimitBytes` | `long` | Caps captured response size; larger bodies still stream to the client but are not captured. Zero = no limit. |
| `FinalizeResponseCapture` | `Func<Task>?` | Set by the Host. A plugin that armed capture should call this right after `next()` returns so it has the body in hand before doing its own post-processing (e.g. the cache publishing to coalesced followers). Idempotent; no-op if the response wasn't produced in-chain — the Host finalizes the fallback-proxy path itself. |

### Naming constraint — `PluginName` + `Variant`

Every plugin has a `Name` matching a value in the closed `PluginName` enum
(`Core/Routing/RoutingEnums.cs`), deserialized strictly via `StrictEnumConverter<T>` —
an unrecognised name in `routes.json` throws at startup, never per-request. Plugins are
registered and resolved under a **`PluginKey`** = `(Name, Variant)`:

- **Built-in plugins** (and anything replacing one) leave `Variant` null.
- **`PluginName.Custom`** is the open-ended extension point: a plugin declares
  `Name => PluginName.Custom` plus a self-chosen `Variant` string (e.g. `"llm-proxy"`).
  Routes select it with `{ "name": "custom", "variant": "llm-proxy" }`. Any number of
  Custom plugins coexist under distinct variants — **no `Core` recompile needed** for a
  genuinely new plugin type, only for a new *built-in* `PluginName` value.
  `GatewayRoutesConfiguration.Validate()` enforces the pairing at startup: `custom`
  requires a variant, every other name must not carry one.
- The PowerShell reference implementation at `examples/ConduitSharp.Plugin.PowerShell`
  (see below) is itself a Custom-variant plugin: routes select it with
  `{ "name": "custom", "variant": "power-shell" }`.

### Registration

**Standalone Host / embedding** wires built-ins via
`GatewayServiceCollectionExtensions.AddConduitSharpGateway` (in
`ConduitSharp.Gateway.AspNetCore`), not `Host/Program.cs` directly — `Program.cs` just
calls `builder.AddConduitSharpGateway()`. To add a plugin in code (e.g. a NuGet plugin):

```csharp
builder.Services.AddSingleton<IPipelinePlugin, MyPlugin>();
```

There is no `http-proxy` plugin. Forwarding is YARP's `ForwarderMiddleware`; the
`"name": "http-proxy"` entry in a route's plugin list only marks **where in the chain** the
forward happens (it must be last — enforced at startup). Omit it and the forward is appended
at the end of the chain anyway.

**External plugins** are loaded at startup from
`{Gateway:PluginsPath}` (default `plugins/` next to the binary), when
`ConduitSharpGatewayOptions.EnablePluginDirectoryScan` is true (default). Every DLL is
loaded into `AssemblyLoadContext.Default` — the shared host context, full trust, no
isolation (isolated contexts break plugins that use native P/Invoke, e.g. the PowerShell
SDK). A `Resolving` handler finds each plugin's private dependencies published alongside
its DLL, and assemblies the host already loaded are reused, so the host and plugin share
the same `IPipelinePlugin` type identity from `ConduitSharp.Core`. Drop the
compiled DLL (and its private dependencies, if any) into `plugins/` and restart the
gateway — no rebuild required. A DLL implementing `ICacheService` dropped in the same
root also overrides the built-in in-memory cache (e.g. `ConduitSharp.Cache.RedisProtocol`).

**Plugin scoping — what is per-route and what is gateway-wide.** Plugin *activation* is
per-route: each route's `plugins` list in routes.json decides which plugins run for that
route, in what order, with what config (jwt-auth, api-key-auth, rate-limit, cache,
header-transform, the forwarder — built-in `http-proxy` or a drop-in like YARP — and
`custom` variants). Plugin *code discovery* is gateway-wide: the `plugins/{routeId}/`
folders only organize DLLs on disk; every discovered `IPipelinePlugin` type joins one
global registry keyed by (name, variant) and is available to **all** routes — dropping a
DLL into `plugins/route-a/` does not restrict it to route-a, and a route only runs it if
routes.json declares it. Finally, *service backends* are gateway-wide by nature: a DLL in
the plugins **root** replaces an instance-wide service — `ICacheService` (cache backend),
`IRateLimitStore` (rate-limit *counter backend*), `IRateLimiter` (rate-limit *algorithm* — fixed
window by default; separate from the store because a store changes to share counters across
replicas while an algorithm changes what "over the limit" means, and an algorithm need not use a
store at all), YARP `ILoadBalancingPolicy` (node selection, opted into per route by name via
`cluster.loadBalancingPolicy`), ASP.NET Core `MatcherPolicy` (custom route matching) — which
applies to every route.

### Overriding a built-in plugin (last-registration wins)

`ResolvePlugin` (GatewayApplicationBuilderExtensions) picks the implementation with
`LastOrDefault` over every DI-registered `IPipelinePlugin`, matching either the plugin's
`Id` against the route's kebab-case name, or the (`Name`, `Variant`) pair. Registration
order is: built-ins first (`AddConduitSharpGateway`), then external DLLs (scanned from
`plugins/`), then anything the embedding host registers in DI *after*
`AddConduitSharpGateway`. Later wins — silently; the startup log line
`Registered plugin '{id}' implementation {type} from {assembly} ({source})` shows which
registration won and whether it came from `built-in`, `plugins-folder`, or `host-di`.
Note the `Id` clause matches on `Id` alone: a plugin declaring `Name = Custom` but
`Id = "cache"` takes over routes that say `"name": "cache"`. You can **replace** any
built-in plugin by registering later with the same `Name` + `Variant` (null for
built-ins):

- **Drop-in DLL**: compile a class library with `Name => PluginName.RateLimit`, drop the
  DLL into `plugins/`, restart. Your implementation replaces the built-in for all routes.
- **DI registration after built-ins**: `builder.Services.AddSingleton<IPipelinePlugin,
  MySlidingWindowPlugin>()` after `AddConduitSharpGateway()` — same effect. This is how
  `examples/EmbeddedGateway` swaps in the YARP forwarder with one line.
- **Integration tests**: `GatewayFactory.CreateAsync(..., plugins: [new
  FixedStatusPlugin(PluginName.JwtAuth, ...)])` — test plugin wins because it's added
  after the app's own DI registrations.

### Adding a genuinely new plugin type

Use `PluginName.Custom` + a self-chosen `Variant` (see "Naming constraint" above) — this
needs **no** `Core` recompile. Adding a new *built-in* enum value still requires adding it
to `PluginName` in `Core/Routing/RoutingEnums.cs` and recompiling `Core` and every package
that consumes it.

### Plugin patterns (reference implementations)

**Header injection** — mutate `context.Request.Headers` before calling `next`:
```csharp
public async Task ExecuteAsync(PluginContext context, PluginDelegate next)
{
    context.Request.Headers["X-Request-Id"] = Guid.NewGuid().ToString();
    await next(context);
}
```

**Short-circuit (block/respond)** — call `ShortCircuit` and return without calling `next`:
```csharp
public async Task ExecuteAsync(PluginContext context, PluginDelegate next)
{
    if (!IsAuthorised(context.Request))
    {
        context.ShortCircuit(401, "Unauthorized.");
        return;
    }
    await next(context);
}
```

**Response capture** — set `ResponseCaptureCallback` before calling `next`, then call
`context.FinalizeResponseCapture?.Invoke()` right after `next()` returns so the body is
in hand before any post-processing:
```csharp
public async Task ExecuteAsync(PluginContext context, PluginDelegate next)
{
    context.ResponseCaptureCallback = async (status, contentType, body) =>
    {
        await _cache.SetAsync(CacheKey(context.Request), body);
    };
    await next(context);
    if (context.FinalizeResponseCapture is { } finalize) await finalize();
}
```

**Custom / terminal handler** — use `PluginName.Custom` with a `Variant`, set
`"cluster": null` on the route, short-circuit with the response body. Covers fan-out
aggregation, PowerShell execution, direct DB calls, or any handler that produces a
response without forwarding:
```csharp
public PluginName Name    => PluginName.Custom;
public string?    Variant => "fan-out";

public async Task ExecuteAsync(PluginContext context, PluginDelegate next)
{
    var config = FanOutConfig.From(context.PluginConfig);
    var tasks  = config.Targets.Select(t => _http.GetStringAsync(t.Url));
    var bodies = await Task.WhenAll(tasks);
    var merged = Merge(config.Targets.Zip(bodies));
    context.ShortCircuit(200, merged);
    // do not call next — no single upstream to forward to
}
```

---

## Route configuration (routes.json)

Path resolution priority:
1. `Gateway:RoutesPath` config value (env override `Gateway__RoutesPath`)
2. `{AppContext.BaseDirectory}/Configuration/routes.json` (default next to binary)
3. When embedding: `ConduitSharpGatewayOptions.Routes` (in-memory table) wins over both.

```jsonc
{
  "routes": [
    {
      "id": "user-service",           // unique, [A-Za-z0-9_-] only; duplicates throw at startup
      "description": "optional label shown in Swagger UI dropdown",
      // --- YARP's RouteConfig, verbatim. routeId/clusterId/order are filled in from `id`
      //     and list position, so they are never written here.
      "route": {
        "match": {
          "path": "/api/users/{**rest}",  // see path matching below
          "methods": ["GET", "POST"],     // omit for any verb
          "headers": [                     // YARP matcher objects, not a dict — so modes work
            { "name": "X-Version", "values": ["2"], "mode": "ExactHeader" }
          ],
          "queryParameters": [
            { "name": "locale", "values": ["en"], "mode": "Exact" }
          ]
        }
        // anything else RouteConfig exposes also works here: hosts, transforms, corsPolicy, ...
      },

      // --- YARP's ClusterConfig, verbatim. null = plugin-only route (YARP never sees it).
      "cluster": {
        "loadBalancingPolicy": "RoundRobin",   // any registered ILoadBalancingPolicy name
        "destinations": {
          "node-0": { "address": "http://svc-1:8080" }   // keys are yours; they show up in traces
        },
        "httpRequest": { "activityTimeout": "00:00:05" },      // per-attempt timeout before 504
        "httpClient": { "dangerousAcceptAnyServerCertificate": false }
        // ...plus sessionAffinity, healthCheck.active, sslProtocols, etc. — all free
      },

      // --- ConduitSharp's own, because YARP has no concept of them.
      "retry": {                          // omit = no retries. Idempotent methods only.
        "maxAttempts": 3,                 // total attempts INCLUDING the first
        "delayMs": 200,
        "backoff": "Exponential",         // Fixed | Linear | Exponential
        "jitter": true,
        "retryOn": [502, 503, 504]
      },
      "circuitBreaker": {                 // omit = no circuit breaking
        "threshold": 5,                   // consecutive failures before a node's circuit opens
        "cooldownMs": 10000               // how long it stays open before one trial request
      },
      "plugins": [
        { "name": "jwt-auth", "order": 1, "enabled": true, "config": { ... } },
        { "name": "rate-limit", "order": 2, "enabled": true, "config": { ... } },
        { "name": "custom", "variant": "fan-out", "order": 99, "config": { ... } }
      ],
      "swagger": {
        "fetchFrom": "http://svc-1:8080/swagger/v1/swagger.json"
        // OR "specFile": "./specs/user-service.json"
      },
      "maxRequestBodyBytes": null      // overrides the global body-size cap for this route; null inherits it
    }
  ]
}
```

### Path matching rules

| Pattern | Example template | Matches |
|---|---|---|
| Literal | `/api/orders` | Exact path only (case-insensitive) |
| Named param | `/api/orders/{id}` | Captures exactly one segment |
| Catch-all | `/api/{**rest}` | Captures zero or more remaining segments |

Routes are evaluated **first-match-wins** in list order. Put more-specific routes
before catch-alls.

### Enum values accepted in JSON

- **`route.match.methods`** — plain strings; YARP validates them
- **`cluster.loadBalancingPolicy`** — any registered `ILoadBalancingPolicy` name: `"RoundRobin"`,
  `"Random"`, `"PowerOfTwoChoices"`, `"LeastRequests"`, `"FirstAlphabetical"`, or a drop-in DLL's
- **`retry.backoff`** — `"Fixed"`, `"Linear"`, `"Exponential"`
- **`plugins[].name`** — `"jwt-auth"`, `"jwks-jwt-auth"`, `"api-key-auth"`, `"api-key-auth-hashed"`, `"rate-limit"`, `"header-transform"`, `"cache"`, `"http-proxy"`, `"custom"` (requires `"variant"`)

The whole document may be written in camelCase — `GatewayRoutesConfiguration.JsonOptions` binds
case-insensitively, which is what lets YARP's attribute-free records sit next to ConduitSharp's
`[JsonPropertyName]`-annotated ones in one file. (Note the converter ordering in `JsonOptions`: a
converter in `Options.Converters` beats a `[JsonConverter]` attribute on the type, so
`StrictEnumConverter<PluginName>` is registered *before* `JsonStringEnumConverter` or kebab-case
plugin names would silently break.)

**Who validates what.** YARP validates its own half at config load — match syntax, destination
addresses, load-balancing policy names. `GatewayRoutesConfiguration.Validate()` covers the rest:
duplicate/invalid route IDs, a `custom` plugin without a `variant` (or a non-`custom` plugin with
one), a cluster with no destinations or a non-http(s) destination scheme, `retry.maxAttempts < 1`,
a `retryOn` entry outside 100–599, and a non-positive `circuitBreaker.cooldownMs`. Both run at
startup *and* on admin reload, so a bad table is rejected before anything is swapped.

---

## Claim-based authorization (`requiredClaims`)

`jwt-auth` and `jwks-jwt-auth` both accept an optional `"requiredClaims"` array in their
config (`RequiredClaim` / `RequiredClaimsValidator`, `Core/Jwt/` — see "Key architectural
decisions" for the 401-vs-403 split). Each rule: `{ "claim": "...", "anyOf"/"allOf"/"equals": [...] }`,
existence-only if no matcher is given. Full matcher semantics and the Auth0/Keycloak/Okta
examples are in the README's [Claim-based authorization (RBAC)](AUTHORIZATION.md#claim-based-authorization-rbac)
section — not duplicated here since it's config-facing, not implementation detail.

**Entra ID (Azure AD) specifics** (also in the README, condensed here for a quick sanity
check when debugging a route): app roles arrive as `"roles": [...]` (array, `anyOf`/`allOf`,
no delimiter); scopes arrive as `"scp": "a b c"` (single space-delimited string, needs
`"delimiter": " "`). Two token-version pairings exist and must match exactly what the
token actually contains (check at jwt.ms, don't assume) — mixing them 401s before
`requiredClaims` is ever evaluated:

| Version | `issuer` | `audience` |
|---|---|---|
| v2.0 | `https://login.microsoftonline.com/<tenant-id>/v2.0` | bare API client-id GUID |
| v1.0 (default) | `https://sts.windows.net/<tenant-id>/` | `api://<api-app-id-uri>` |

An unassigned user's token omits the `roles` claim entirely (not an empty array) — that's
the expected `403 Missing required claim 'roles'.` path, not a bug.

---

## Reliability: retries and circuit breaking

Per-route, configured on `upstream` (see fields above). YARP ships neither, so both wrap its
forwarder — `Proxy/UpstreamRetry.cs` and `Proxy/ConsecutiveFailuresHealthPolicy.cs`.

- **Retries** apply only to idempotent methods (`GET`, `HEAD`, `OPTIONS`, `PUT`, `DELETE`,
  `TRACE`) — a `POST`/`PATCH` may already have been processed upstream, so resending could
  double-apply it. A per-route Polly `ResiliencePipeline<int>` schedules attempts
  (`maxAttempts`, `delayMs`, `backoff` = Fixed/Linear/Exponential, `jitter`, `retryOn`); the bare
  The loop owns what Polly cannot see: rewinding the buffered
  request body, restoring `AvailableDestinations` so load balancing re-picks (failover across
  nodes), and resetting the status/headers a failed attempt left behind.

  A retried attempt must never reach the client, but YARP's forwarder copies the status and
  headers before any transform runs. `SuppressRetriedResponseTransform` (an `ITransformProvider`)
  sets `SuppressResponseBody` when a retry is still possible and the status is in `retryOn`, so
  the forwarder returns `ForwarderError.None` **without starting the response** — leaving the
  retry loop free to reset it. Timeout → 504, transport failure → 502 (YARP's own mapping).
- **Circuit breaker** — `ConsecutiveFailuresHealthPolicy`, a YARP `IPassiveHealthCheckPolicy`
  named `"ConsecutiveFailures"`. YARP's stock `TransportFailureRate` policy is
  rate-over-a-window and cannot express a consecutive-failure threshold, so this one counts them:
  after `circuitBreaker.threshold` in a row it calls `IDestinationHealthUpdater.SetPassive(...,
  Unhealthy, reactivationPeriod: circuitBreakerCooldownMs)`, dropping the destination from the
  load balancer's available set. Reactivation resets it to `Unknown` (not Healthy) and the
  counter stays at the threshold, so a node that fails its trial request re-opens immediately
  while one that succeeds resets. A client disconnect (`RequestAborted`) is never counted.
  An absent `circuitBreaker` block (or `threshold <= 0`) disables passive health checks entirely.
  The threshold and cooldown are read from the route's own typed `CircuitBreakerConfig` via
  `GatewayRouteTable.TryGetRoute(cluster.ClusterId)` — **nothing rides on
  `ClusterConfig.Metadata`**. See "Two halves of a route's config" below.

---

## Two halves of a route's config

The split is structural, not conventional — it is literally the shape of the type:

```csharp
public sealed class GatewayRoute
{
    public required string        Id      { get; init; }
    public required RouteConfig   Route   { get; init; }   // YARP's, verbatim
    public          ClusterConfig? Cluster { get; init; }  // YARP's, verbatim

    public RetryConfig?          Retry          { get; init; }   // ours
    public CircuitBreakerConfig? CircuitBreaker { get; init; }   // ours
    public List<PluginConfig>    Plugins        { get; init; }   // ours
    public SwaggerOptions?       Swagger        { get; init; }
    public long?                 MaxRequestBodyBytes { get; init; }
}
```

**Retry sits *beside* the cluster, not inside it.** That is what makes the whole design work:
`ClusterConfig` has no retry field, and its `Metadata` (an `IReadOnlyDictionary<string,string>`)
could never hold a structured policy with a status-code array. Composition solves what embedding
could not.

**One cluster per route, `ClusterId == RouteId`.** The other half of the trick: any code reached
from YARP's side knows a cluster id, so it can get straight back to typed ConduitSharp config with
`GatewayRouteTable.TryGetRoute(clusterId)`. Nothing has to travel *through* YARP — and so
`ClusterConfig.Metadata` is never written to at all.

**Why YARP's types and not our own DTOs?** A hand-rolled schema is a projection, and a projection
is a layer that can disagree with YARP. It already did once: `ForwarderRequestConfig` is per-cluster
but the correct outbound HTTP version depends on the *inbound* request, so the translator silently
downgraded HTTP/2 and broke gRPC — caught only by the LegacyGateway e2e suite. It also had to grow a
field every time YARP grew one. Using YARP's records directly deletes both problems, and every YARP
feature (session affinity, active health checks, transforms, `sslProtocols`, host matching) becomes
configurable with no work here.

**Why is the schema in `Gateway.AspNetCore` and not `Core`?** Because it is built on YARP's types,
and `Core` is what plugin authors compile against — it has *zero* NuGet dependencies and should keep
them. Plugins never needed the route anyway: every one of them only ever read `route.Id`, so the
plugin contract is `Items["ConduitSharp.RouteId"]`, a `string`.

**YARP's native per-route policies still work — they ride the `route` block.** Because `route` is a
verbatim `RouteConfig`, `authorizationPolicy` / `rateLimiterPolicy` / `corsPolicy` / `timeout`
deserialize and are handed to YARP untouched. They are enforced by the framework's own middleware,
not by us: in embedded mode a host that calls `AddAuthorization` / `AddRateLimiter` (WebApplication
auto-inserts the enforcement middleware) gets them applied to the matched proxy endpoint. So the
native model and the plugin chain coexist on the same route — plugins are not a reimplementation of
policies, they are the data-configured, hot-reloadable, standalone-host-usable alternative axis.
Pinned by `NativePolicyPassthroughTests`.

**Nothing is written to `ClusterConfig.Metadata`, deliberately.** It is an
`IReadOnlyDictionary<string, string>`: it cannot hold the structured JSON a plugin's config block
is, and for the values it *can* hold it trades compile-time typing for a parse on the hot path.
The circuit-breaker threshold used to ride across that way; it does not any more.

**Why not `IOptions`?** It was considered as the container for the gateway half and rejected.
`GatewayRoute` has `required` members, so `OptionsFactory` can only build it through
`Activator.CreateInstance<T>()` — an instance with `Id`/`Match` null that then has to be populated.
And `IOptions`' real payoff is change-tracking bound to `IConfiguration`, whereas this config comes
from a file the gateway parses itself plus an admin endpoint that hot-swaps it; the reload
notification would have to be reimplemented anyway, which `GatewayRouteTable` already does
atomically (see Admin API). A named-options lookup on the request path is also strictly more work
than the frozen dictionary that is already there.

---

## Observability

### OpenTelemetry

`GatewayTelemetry` (`ConduitSharp.Observability.Telemetry`) and `PipelineTelemetry`
(`ConduitSharp.Core.Pipeline`) each hold an `ActivitySource`; `GatewayTelemetry` also
holds a `Meter`. Both sources are named `"ConduitSharp.Gateway"` /
`"ConduitSharp.Pipeline"` respectively and are zero-overhead when no listener is attached.

**Traces:**
- `GatewayMiddleware` starts a `gateway.request` activity per request. Tags:
  `http.request.method`, `url.path`, `conduitsharp.route_id` (set after route match),
  `http.response.status_code`, `conduitsharp.short_circuited` (when applicable). Status
  set to `Error` only on 5xx (Ok is reserved for explicit operator vetting).
- `PluginPipelineExecutor` starts one `plugin.{Name}` (or `plugin.{Name}:{Variant}` for
  Custom) child span per plugin invocation, tagged `conduitsharp.plugin` — gives
  per-plugin latency breakdown inside a request's trace.
- Admin route reloads add an `admin.routes.reloaded` activity event with
  `conduitsharp.route_count` and `client.address` tags.

**Metrics** — `OtelMetricsObserver` implements `IRequestObserver` and records to:

| Instrument | Type | Tags |
|---|---|---|
| `conduitsharp.gateway.requests` | Counter | `route_id`, `http.request.method`, `http.response.status_code` |
| `conduitsharp.gateway.request.duration` | Histogram (ms) | same |
| `conduitsharp.gateway.errors` | Counter | same (only recorded on 5xx) |

`GatewayTelemetry.AdminReloadCounter` also increments on every successful admin reload.

**OTLP export** is configured in `GatewayServiceCollectionExtensions.AddObservability`
(called from `AddConduitSharpGateway` unless `ConduitSharpGatewayOptions
.ConfigureObservability = false`). Enabled when `Gateway:Observability:Otlp:Enabled` is
true or `OTEL_EXPORTER_OTLP_ENDPOINT` is set — accepts any OTLP-compatible backend
(Aspire dashboard, Jaeger, Grafana Tempo, Datadog, Honeycomb). A console exporter
(`Gateway:Observability:Console:Enabled`) and a file exporter that writes JSON-lines
spans (`Gateway:Observability:File:Enabled`, path `Gateway:Observability:File:TracesPath`)
are also available, independent of OTLP.

**W3C traceparent** propagation to upstream services is automatic via
`AddHttpClientInstrumentation()`.

### IRequestObserver

```csharp
public interface IRequestObserver
{
    void OnRequestCompleted(RequestObservation observation);
}
```

`RequestObservation` fields: `RequestId` (from `HttpContext.TraceIdentifier`), `Method`,
`Path`, `RouteId` (null if unmatched), `StatusCode`, `DurationMs`, `WasShortCircuited`.

Multiple observers are registered as `IEnumerable<IRequestObserver>` in DI. Current
registrations: `StructuredRequestLogger` (structured JSON logs) and `OtelMetricsObserver`
(OTel instruments).

### Structured logging

`StructuredRequestLogger` emits `StructuredLogEntry` JSON for every request. Fields include:
`timestamp`, `method`, `path`, `statusCode`, `durationMs`, `routeId`.

---

## Request body limits

Configured under `Gateway:RequestLimits` (`RequestLimitsOptions`):

- **`MaxRequestBodyBytes`** (default 8 MiB) — per-request cap; a route's
  `maxRequestBodyBytes` overrides it. Over the limit → 413. Zero/negative disables the
  per-request check.
- **`MaxTotalBufferedBodyBytes`** (default 128 MiB) — aggregate cap across all
  concurrently in-flight bodies (RAM + spill), enforced by `RequestBodyBudget` (an
  interlocked counter). A request that would push the total over budget is rejected with
  503 (retryable). Zero/negative disables it — meaning *unlimited*, not zero.
- **`MaxMemoryBufferedBodyBytes`** (default 64 MiB) — the RAM tier, carved out of the
  total. While it has headroom a body buffers in memory (~3–5x faster than spilling);
  once full, further bodies spill from the first byte but are still served. This is what
  bounds buffering's RAM footprint, which is why `MemoryBufferThresholdBytes` can be
  generous per request. Zero/negative disables the tier — meaning *no RAM at all*, the
  opposite direction from the total.
- **`MemoryBufferThresholdBytes`** (default 1 MiB, clamped `[4 KiB, 1 MiB]`) — per-request
  RAM ceiling. The 1 MiB clamp is structural: `FileBufferingReadStream` serves thresholds
  up to 1 MiB from `ArrayPool`, and above it grows a bare `MemoryStream` by doubling,
  allocating ~2x the body on the LOH. A body whose `Content-Length` already exceeds the
  threshold skips the RAM buffer and spills immediately.
- **`SpillDirectory`** (default: system temp) — where the disk tier writes. In containers
  `/tmp` is often `tmpfs` (RAM), which turns the disk tier back into a memory tier and
  OOMs instead of degrading. Point it at a real volume.

Both are enforced in `GatewayMiddleware` before the plugin pipeline runs (bodies are
buffered in memory; Kestrel's own transport-level limit, ~28.6 MB by default, applies
first regardless).

---

## Response capture (middleware-owned tee)

The cache plugin (and any future response-observing plugin) needs the response body
without buffering it twice or requiring the response-producing plugin to cooperate.
`GatewayMiddleware` wraps `IHttpResponseBodyFeature` in `CapturingResponseBodyFeature`
before the plugin chain runs — dormant unless a plugin sets
`PluginContext.ResponseCaptureCallback`. It intercepts **both** write surfaces
(`Response.Body`, a `Stream`, used by the built-in proxy; `Response.BodyWriter`, a
`PipeWriter`, used by YARP's `IHttpForwarder`), so any response producer is capturable
with zero cooperation. Capture is finalized (callback invoked) at the earlier of: the
requesting plugin calling `context.FinalizeResponseCapture()` right after `next()`
returns, or `GatewayMiddleware` itself after the fallback proxy completes. Only a 2xx
response within `ResponseCaptureLimitBytes` is captured; larger or non-2xx responses
still stream to the client, just uncaptured.

---

## Swagger aggregation (optional add-on)

`ConduitSharp.Gateway.AspNetCore.Swagger` — `app.UseConduitSharpGatewaySwagger()`,
called **before** `app.UseConduitSharpGateway()`. It is a no-op when no routes have a
`swagger` block. The standalone `Host` references this package so `/swagger` works out
of the box; embedders who don't want the Swashbuckle dependency simply don't reference it.

Two middleware layers, both falling through to `next()` for non-matching paths:

1. **Spec proxy** — intercepts `GET /swagger/{routeId}.json`:
   - `fetchFrom` → fetches from the upstream URL via `IHttpClientFactory`. Guarded
     against SSRF: the target host must be loopback, one of the route's own upstream
     node hosts, or explicitly listed in `Gateway:Swagger:AllowedSpecHosts` — anything
     else is refused with 403 before any network call.
   - `specFile` → reads a local file, resolved relative to
     `Gateway:BasePath`/`Directory.GetCurrentDirectory()`; guarded against path traversal.
   - Returns 502 with an error message if the upstream fetch fails.
2. **Swagger UI** — serves the Swagger UI static assets at `/swagger`. Each
   swagger-enabled route appears as a dropdown entry labelled `"{id} — {description}"`.

Route `SwaggerOptions` model (in `Core/Routing/GatewayRoute.cs`):
```csharp
public sealed class SwaggerOptions
{
    public string? FetchFrom { get; init; }  // URL — fetched live per request
    public string? SpecFile  { get; init; }  // local file path
}
```

---

## Health endpoints

Mapped by `GatewayApplicationBuilderExtensions.UseConduitSharpGateway` when
`ConduitSharpGatewayOptions.MapHealthEndpoints` is true (default), always at the app
root regardless of `PathPrefix`:

- **`GET /healthz`** — liveness: always 200 if the process is up. Never touches upstreams.
- **`GET /readyz`** — readiness: 200 if a route table is loaded (`Routes.Count > 0`), else
  503. Deliberately independent of upstream reachability — a downstream blip must not
  pull every gateway replica out of rotation.

---

## Admin API

Registered as `Use()` middleware before `GatewayMiddleware`, active only when
`Gateway:AdminKeyHash` (SHA-256 hex hash of the secret, env override
`Gateway__AdminKeyHash`) is non-empty at startup. The incoming `X-Admin-Key` header is
SHA-256 hashed and compared to the configured hash — the raw secret is never stored.
When `Gateway:AdminKeyHash` is not set, the middleware is not registered at all and
`/admin/**` falls through to `GatewayMiddleware` as normal traffic.

**`DELETE /admin/cache/{routeId}`**
- Flushes all cached response entries for the given route (`ICacheService
  .RemoveByPrefixAsync(routeId + '\0', ...)`).
- Logs the removal count and caller IP; returns 200 with the count.

**`POST /admin/routes/reload`**
- Checks `X-Admin-Key` → 401 if missing/mismatched.
- Deserializes and validates the body as `GatewayRoutesConfiguration`, then runs the same
  `ValidatePluginChains` gate startup uses (every named plugin resolves, its config parses,
  `http-proxy` sits last) → 400 if invalid. Nothing is swapped on failure.
- Writes to a temp file in the same directory then atomically renames over
  `routesPath` — a crash mid-write leaves the previous valid file intact rather than
  a corrupt one.
- Logs an audit entry (structured log + `GatewayTelemetry.AdminReloadCounter` + an
  `admin.routes.reloaded` activity event) with route count and caller IP.
- Calls `GatewayRouteTable.Load(...)` → **hot swap, no restart** → 200 `Routes reloaded.`

`GatewayRouteTable` owns everything a reload must swap together, which is the whole reason it
exists: the compiled per-route plugin chains, the route-id lookup, `UpstreamRetry`'s Polly
pipelines, the plugin-only `EndpointDataSource`, and YARP's `InMemoryConfigProvider`. Chains and
lookups are replaced *before* YARP's config, so a request matching a just-added route always finds
its chain. In-flight requests keep the chain and `GatewayRoute` they started with.

**A reload cannot introduce a new plugin DLL or an mTLS client certificate** — both are resolved
from DI at startup, so adding one still needs a restart. `Gateway:ShutdownTimeoutSeconds`
(default 30) governs in-flight draining on an actual shutdown.

---

## Distribution / deployment

### Packages

| Package | What it is |
|---|---|
| `ConduitSharp.Core` | Contracts + pure domain logic; no ASP.NET dependency |
| `ConduitSharp.Gateway.AspNetCore` | The embeddable gateway library (`AddConduitSharpGateway`/`UseConduitSharpGateway`) |
| `ConduitSharp.Gateway.AspNetCore.Swagger` | Optional add-on: aggregated Swagger UI |
| `ConduitSharp.Host` | Thin executable shell; ships as the `conduitsharp` dotnet tool and Docker image |

### Release artefacts (produced by `.github/workflows/release.yml` on `v*` tag push)

| Artefact | How produced |
|---|---|
| `conduitsharp-vX.X.X-win-x64.zip` | `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true` |
| `conduitsharp-vX.X.X-linux-x64.tar.gz` | Same for `linux-x64` |
| `ghcr.io/liqngliz/conduitsharp:{version}` | Multi-stage Docker build, `aspnet:10.0` base, arch-portable (amd64/arm64) |
| `ConduitSharp.Gateway` NuGet tool package | `dotnet pack` with `<PackAsTool>true</PackAsTool>` |
| `ConduitSharp.Gateway.AspNetCore` / `.Swagger` / `ConduitSharp.Core` NuGet packages | `dotnet pack`; all `ConduitSharp.*` packages share one `<Version>` from the root `Directory.Build.props` |

### Routes file in self-contained builds

`Configuration/routes.json` is declared as a `Content` item with
`CopyToPublishDirectory=PreserveNewest`. It is emitted alongside the binary in all
publish modes. Users edit this file or override via `Gateway:RoutesPath` /
`Gateway__RoutesPath`.

### dotnet tool

```
dotnet tool install -g ConduitSharp.Gateway
conduitsharp
```

### NuGet publish (keyless OIDC)

The release workflow uses GitHub OIDC tokens — no `NUGET_API_KEY` secret stored.
Requires a **Trusted Publisher** configured on nuget.org pointing at this repo and
`release.yml`. The OIDC token is exchanged for a temporary key server-side:

```yaml
permissions:
  id-token: write

- run: |
    OIDC_TOKEN=$(curl -s "${ACTIONS_ID_TOKEN_REQUEST_URL}&audience=nuget.org" \
      -H "Authorization: Bearer ${ACTIONS_ID_TOKEN_REQUEST_TOKEN}" | jq -r '.value')
    dotnet nuget push nupkg/*.nupkg --api-key "$OIDC_TOKEN" ...
```

---

## Project structure and dependency graph

```
src/
  ConduitSharp.Core                       ← contracts + pure domain logic; NO ASP.NET dependency
  ConduitSharp.Traffic                    → Core   (rate limiting, caching)
  ConduitSharp.Security                   → Core   (JWT HS256, JWKS RS/ES, API key plain+hashed)
  ConduitSharp.Transformation             → Core   (header-transform plugin)
  ConduitSharp.Observability              → Core   (logging, metrics, OTel instruments, IRequestObserver)
  ConduitSharp.Gateway.AspNetCore         → Core + all feature packages  (embeddable library:
                                             middleware, YARP proxy wiring, plugin loader, options)
  ConduitSharp.Gateway.AspNetCore.Swagger → Gateway.AspNetCore  (optional add-on: aggregated Swagger UI)
  ConduitSharp.Host                       → Gateway.AspNetCore + Swagger add-on  (thin executable;
                                             dotnet tool + Docker image)

tests/
  ConduitSharp.Core.Tests           → Core          (unit: routing, pipeline, deserialization, ValidateConfig)
  ConduitSharp.Security.Tests       → Security       (unit: JWT, JWKS, API key handlers and plugins)
  ConduitSharp.Traffic.Tests        → Traffic        (unit: cache, rate-limit)
  ConduitSharp.Transformation.Tests → Transformation (unit: header-transform plugin)
  ConduitSharp.Observability.Tests  → Observability  (unit: logging middleware, metrics collector)
  ConduitSharp.Integration.Tests    → Host           (end-to-end via WebApplicationFactory)
  ConduitSharp.LegacyGateway.E2E.Tests  → out-of-process E2E against the real gateway binary
  ConduitSharp.Grafana.E2E.Tests    → Docker observability-pipeline E2E (Tempo/Prometheus/Loki)
  ConduitSharp.Mtls.E2E.Tests       → Docker mTLS E2E (real client-cert handshake; cross-platform)

examples/
  EmbeddedGateway/                   — embeds the gateway in a plain ASP.NET Core app and adds
                                       the Redis cache plugin from NuGet, in code
  ConduitSharp.Plugin.PowerShell/    — custom:power-shell drop-in that runs a .ps1 in-process
                                       via the embedded Microsoft.PowerShell.SDK (no system pwsh
                                       required); short-circuits with the script's output
  ConduitSharp.Cache.RedisProtocol/  — drop-in distributed ICacheService (Valkey / Redis 7 / RESP)
  ConduitSharp.RateLimit.RedisProtocol/ — drop-in distributed IRateLimitStore (shared limits, fail-open)
  LegacyGateway/                         — runnable multi-route demo stack (make run / start.ps1)
```

Rule: **feature packages reference only Core**; they never reference each other or
the gateway library. `ConduitSharp.Gateway.AspNetCore` is the single aggregation point;
`Host` adds nothing but the executable entry point and its `Configuration/` packaging
convention. All `ConduitSharp.*` packages version together from a single `<Version>` in
the root `Directory.Build.props`. Test projects follow the same pattern — one unit-test
project per source package, plus E2E projects that exercise the real binary/Docker stack.

### What is implemented vs. stub

| Package | Status |
|---|---|
| `Core` | **Fully implemented** — routing, pipeline executor, all types |
| Load balancing | **YARP** — `RoundRobin` (default), `Random`, `PowerOfTwoChoices`, `LeastRequests`, `FirstAlphabetical`, or a drop-in `ILoadBalancingPolicy` DLL named in `cluster.loadBalancingPolicy` |
| `Traffic` — rate limiting | **Implemented** — `RateLimitPlugin`, `FixedWindowRateLimiter` (injectable clock, eviction), `ValidateConfig` rejects non-positive window/max |
| `Traffic` — caching | **Implemented** — `CachePlugin`, `InMemoryCacheService`, `ValidateConfig` rejects non-positive ttl; distributed backend available as a drop-in (`ConduitSharp.Cache.RedisProtocol`) |
| `Security` — JWT (HS256) | **Implemented** — `JwtAuthPlugin`, `JwtAuthHandler`, `JwtClaimsValidator`, `JwtBase64Url` |
| `Security` — JWT (RS/ES via JWKS) | **Implemented** — `JwksJwtAuthPlugin`, `JwksJwtAuthHandler`, `JwksSignatureVerifier`, `JwksKeyProvider`, `ValidateConfig` requires `jwksUri` |
| `Security` — claim-based RBAC | **Implemented** — `RequiredClaim` + `RequiredClaimsValidator`, shared by both JWT plugins via `requiredClaims` config; failure is 403 (permission), not 401 (authentication) |
| `Security` — API key (plain) | **Implemented** — `ApiKeyAuthPlugin`, `ApiKeyAuthHandler` |
| `Security` — API key (hashed) | **Implemented** — `ApiKeyAuthHashedPlugin`, `ApiKeyAuthHashedHandler` |
| `Transformation` — header-transform | **Implemented** — `HeaderTransformPlugin` (add, remove, rewrite) |
| `Custom` | **No core-provided implementation** — `PluginName.Custom` + `Variant` is the escape hatch for terminal handlers (fan-out, DB, COM); implement as a drop-in DLL |
| `Custom` — `power-shell` variant | **Example implementation** at `examples/ConduitSharp.Plugin.PowerShell` — runs a `.ps1` in-process via the embedded PowerShell SDK; see ARCHITECTURE.md for production-hardening considerations (runspace pooling, out-of-process execution) before heavy concurrent/ETL use |
| `Observability` — structured logging | **Implemented** — `StructuredRequestLogger`, `StructuredLogEntry` |
| `Observability` — OpenTelemetry | **Implemented** — `GatewayTelemetry` + `PipelineTelemetry` (per-plugin spans), `OtelMetricsObserver`, console/file/OTLP exporters |
| `Gateway.AspNetCore` — embeddable library | **Implemented** — `AddConduitSharpGateway`/`UseConduitSharpGateway` + `ConduitSharpGatewayOptions` composition knobs |
| `Gateway.AspNetCore` — gateway middleware | **Implemented** — OTel tracing, request body limits/budget, `HasStarted` forwarding fallback, middleware-owned response capture |
| `Gateway.AspNetCore` — forwarding | **YARP `IHttpForwarder`** — HTTP/2, gRPC, WebSockets, streaming, trailers. Around it: `YarpConfigTranslator`, `UpstreamRetry` (Polly), `ConsecutiveFailuresHealthPolicy`, `UpstreamForwarderHttpClientFactory` (mTLS), `UpstreamProtocol` (h2c) |
| `Gateway.AspNetCore` — admin API | **Implemented** — `POST /admin/routes/reload` (atomic file swap, audit trail), `DELETE /admin/cache/{routeId}`, gated by `Gateway:AdminKeyHash` |
| `Gateway.AspNetCore` — health endpoints | **Implemented** — `/healthz`, `/readyz` |
| `Gateway.AspNetCore` — plugin loader | **Implemented** — `PluginAssemblyLoader` (loads into the shared default `AssemblyLoadContext`; no isolation) |
| `Gateway.AspNetCore.Swagger` — add-on | **Implemented** — `UseConduitSharpGatewaySwagger()`; `SwaggerAggregationExtensions` (fetchFrom + specFile modes, SSRF/path-traversal guards) |
| `Host` — standalone shell | **Implemented** — thin executable over the library; dotnet tool + arch-portable Docker image |

---

## Key architectural decisions

**PluginName is a closed enum; `Custom` + `Variant` is the open extension point.**
Names are validated at deserialization time via `StrictEnumConverter<T>`. Invalid names
throw `JsonException` at startup, never at request time. Plugins register/resolve under
a `PluginKey` = `(PluginName, Variant)`; any number of `Custom` plugins coexist under
distinct variants with **no `Core` recompile**. Adding a new *built-in* `PluginName`
value still requires editing the enum and recompiling `Core`.

**GatewayRequest has no ASP.NET dependency.** `RouteMatcher` and `PluginContext`
operate on `GatewayRequest`, a plain DTO. The gateway library maps `HttpRequest →
GatewayRequest` before entering the pipeline. This keeps Core independently testable
without a web host.

**First-match-wins routing.** Route priority is determined by position in
`routes.json`. There is no scoring or specificity algorithm — put catch-alls last.

**Claim-based authorization is 403, authentication failure is 401.** `JwtAuthHandler
.TryValidate` and `JwksJwtAuthHandler.TryValidateAsync` both return the verified claims
`JsonElement` on success (cloned before the backing `JsonDocument` is disposed) so the
plugin can run `RequiredClaimsValidator` afterward without re-parsing the token. Signature/
exp/nbf/iss/aud failures stay 401 inside the handler; a `requiredClaims` failure is checked
by the plugin *after* the handler succeeds and short-circuits 403 — the token is valid, the
caller just lacks permission for this route. `RequiredClaim.ValidateAll` runs at config-load
time (both plugins' `From()` and `ValidateConfig`), so a malformed `requiredClaims` block
fails at startup like any other plugin config error.

**Plugin chain is a delegate chain built in reverse.** `PluginPipelineExecutor` sorts
plugins by `Order` ascending, then wraps them inside-out into a nested `PluginDelegate`
so the lowest-order plugin is the outermost call. `PluginConfig` on `PluginContext` is
reassigned before each plugin is invoked — plugins that need their config after calling
`next` must capture it in a local variable first. Each plugin invocation is also wrapped
in a `plugin.{Name}` OTel span.

**Per-plugin config validation is opt-in but startup-enforced when present.**
`IPipelinePlugin.ValidateConfig` defaults to a no-op; `PluginPipelineExecutor
.ValidateRouteConfigs` runs it for every enabled plugin at startup and on admin
reload, wrapping failures with route/plugin context so a bad config value fails the
deploy/reload instead of the first request that hits it.

**The forward runs INSIDE the plugin chain's `next()`.** A route's chain is compiled once into a
`RequestDelegate` whose terminal step invokes `Items["ConduitSharp.ProxyNext"]` — the continuation
into the rest of YARP's proxy pipeline. Consequences: a plugin short-circuits by simply not calling
`next()`, and the cache plugin's `CapturingStream` tee wraps the *real* forward, so coalescing
always engages.

**Routes with `"cluster": null` never reach YARP.** YARP rejects a route with no cluster before
any middleware runs (503, no plugins), so plugin-only routes are served as ordinary endpoints from
`GatewayRouteTable.PluginEndpoints` (a mutable `EndpointDataSource`) running the same compiled
chain. That data source is also what lets an admin reload add and remove them.

**gRPC needs per-request protocol selection.** A cluster's `ForwarderRequestConfig` is static, but
the right outbound protocol depends on how the client arrived. YARP's default (HTTP/2,
`RequestVersionOrLower`) silently downgrades to HTTP/1.1 against a cleartext upstream — no ALPN to
negotiate with — which is right for a normal request and fatal for gRPC. `UpstreamProtocol` swaps
`ReverseProxyFeature.Cluster` for an h2c-exact model (`RequestVersionExact`) on inbound HTTP/2,
reusing the cluster's own `HttpMessageInvoker`.

**No catch-all 404 endpoint.** One would match every path and mask endpoint routing's own 405, so
"right path, wrong verb" would return 404. Unmatched requests get the framework's 404 and, under a
`PathPrefix`, fall through to the host app.

**One HttpMessageInvoker per cluster — no named HttpClients.** Upstream forwarding does not use
`IHttpClientFactory` (only the `"jwks"` client remains). YARP builds one invoker per cluster from
`HttpClientConfig`, so `skipCertificateVerification` maps to `DangerousAcceptAnyServerCertificate`.
`HttpClientConfig` has no client-certificate knob, so `UpstreamForwarderHttpClientFactory` (an
`IForwarderHttpClientFactory`) attaches the route's cert to the cluster's `SocketsHttpHandler` —
clusters are keyed by route id. Certs load when YARP builds the client at config load, so a bad
path fails the gateway at startup, not on the first request. Configuring both a client cert and
`skipCertificateVerification` on the same route is rejected at startup.

**No assembly isolation — plugins share the host's context.** Every DLL in `plugins/`
is loaded into `AssemblyLoadContext.Default` with full trust; isolated contexts were
deliberately abandoned because they break native P/Invoke (e.g. the PowerShell SDK).
Sharing one context also guarantees the plugin's `IPipelinePlugin` has the same type
identity as the host's. The security model is therefore: only deploy plugin DLLs from
sources you control.

**Hop-by-hop headers are stripped before forwarding.** `BuildUpstreamRequest` removes
`Connection`, `Keep-Alive`, `Transfer-Encoding`, and related headers. The `Host` header
is also removed so the upstream sees its own hostname.

---

## Integration test patterns

Tests use `WebApplicationFactory<Program>` via `GatewayFactory` and a real in-process
HTTP server (`FakeUpstream`) as the upstream stand-in.

```csharp
// Standard setup (in IAsyncLifetime.InitializeAsync)
_upstream = await FakeUpstream.StartAsync();
_factory  = await GatewayFactory.CreateAsync(_upstream);   // catch-all passthrough route
_client   = _factory.CreateClient();

// Override what the upstream returns
_upstream.RespondWith(503);
_upstream.RespondWith(async ctx => { ... });

// Inspect what the upstream received
Assert.Single(_upstream.ReceivedRequests);
Assert.Equal("POST", _upstream.ReceivedRequests[0].Method);

// Custom routes JSON
await using var factory = await GatewayFactory.CreateAsync(_upstream, routesJson);

// Inject test plugins (for short-circuit / pipeline tests)
await using var factory = await GatewayFactory.CreateAsync(
    _upstream, routesJson, plugins: [new MyTestPlugin()]);

// Override upstream HttpClient timeout (to exercise 504 path)
await using var factory = await GatewayFactory.CreateAsync(
    _upstream, upstreamHttpTimeout: TimeSpan.FromMilliseconds(150));

// Arbitrary config overrides (e.g. request body limits), keyed like appsettings paths
await using var factory = await GatewayFactory.CreateAsync(
    _upstream, settings: new Dictionary<string, string?> { ["Gateway:RequestLimits:MaxRequestBodyBytes"] = "10" });

// Admin API tests — set env var before factory creation so it's read at startup
Environment.SetEnvironmentVariable("Gateway__AdminKeyHash", TestAdminKeyHash);
await using var factory = await GatewayFactory.CreateAsync(_upstream);
// ... clear in DisposeAsync
```

`Gateway__RoutesPath` env var is set by `GatewayFactory.CreateAsync` to a temp file and
left for the process (each test spins up its own `WebApplicationFactory`).
`xunit.runner.json` disables parallel test collection
(`"parallelizeTestCollections": false`) to prevent race conditions when multiple test
classes write process-level env vars concurrently.

### OTel tracing in tests

To cover non-null activity branches in `GatewayMiddleware` or `PluginPipelineExecutor`,
register an `ActivityListener` before creating the gateway client:

```csharp
using var listener = new ActivityListener
{
    ShouldListenTo  = source => source.Name is GatewayTelemetry.SourceName or PipelineTelemetry.SourceName,
    Sample          = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStarted = _ => { },
    ActivityStopped = a => spans.Add(a),
};
ActivitySource.AddActivityListener(listener);
```

### Coverage notes

- `PluginAssemblyLoader`'s `ReflectionTypeLoadException` catch is intentionally
  un-covered — requires an assembly whose type dependencies are absent, not producible
  through the normal test surface without emitting IL at test time.
- CI now generates a coverage report automatically on every run (see
  `.github/workflows/ci.yml`) — treat that as the source of truth for current line/branch
  coverage rather than a number pinned in this doc, since it drifts with every PR.

# Configuring routes

_Part of the [ConduitSharp documentation](../README.md)._


All routing lives in `Configuration/routes.json`, next to the binary. No database, no admin UI. A file you commit, review, and diff.

```json
{
  "routes": [
    {
      "id": "user-service-route",
      "description": "Public user profile endpoint",

      "route": {
        "match": {
          "path": "/api/users/{**catch-all}",
          "methods": [ "GET", "POST" ]
        }
      },

      "cluster": {
        "loadBalancingPolicy": "RoundRobin",
        "destinations": {
          "node-0": { "address": "http://user-service-1:8080" },
          "node-1": { "address": "http://user-service-2:8080" }
        },
        "httpRequest": { "activityTimeout": "00:00:05" }
      },

      "retry":          { "maxAttempts": 2, "delayMs": 100 },
      "circuitBreaker": { "threshold": 5, "cooldownMs": 10000 },

      "plugins": [
        { "name": "jwt-auth",   "enabled": true, "order": 1, "config": { "issuer": "https://auth.example.com" } },
        { "name": "rate-limit", "enabled": true, "order": 2, "config": { "requestsPerWindow": 100 } },
        { "name": "http-proxy", "enabled": true, "order": 99 }
      ]
    }
  ]
}
```

A route has two halves:

- **`route` and `cluster` are YARP's own `RouteConfig` and `ClusterConfig`**, verbatim. Nothing is
  projected or re-modelled, so every YARP feature is available the day YARP ships it: session
  affinity, active health checks, request/response transforms, host matching, `sslProtocols`,
  per-destination `host` overrides. Header and query matching get YARP's full matcher objects
  (`ExactHeader`, `Prefix`, `Contains`, `NotExists`), not just exact string equality. `routeId`,
  `clusterId` and `order` are derived from the route's `id` and its position in the file, never
  typed.
- **Everything else is ConduitSharp's**, because YARP has no concept of it: `retry`,
  `circuitBreaker`, `plugins`, `swagger`, `maxRequestBodyBytes`.

Write it all in camelCase; YARP's records bind case-insensitively.

> **Upgrading from 0.1.4?** The schema changed. See the
> [migration guide](../CHANGELOG.md#migrating-routesjson).

### Where the file is read from

1. `Gateway:RoutesPath` config value (env override `Gateway__RoutesPath`)
2. `{AppContext.BaseDirectory}/Configuration/routes.json`, next to the binary
3. When embedding, `ConduitSharpGatewayOptions.Routes` (in-memory table) wins over both

### Every field

The reference the rest of the docs point at. Only `id` and `route` are required.

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
          "path": "/api/users/{**rest}",  // see "Path syntax" below
          "methods": ["GET", "POST"],     // omit for any verb
          "headers": [                    // YARP matcher objects, not a dict — so modes work
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
        { "name": "jwt-auth", "order": 1, "enabled": true, "config": { } },
        { "name": "rate-limit", "order": 2, "enabled": true, "config": { } },
        { "name": "custom", "variant": "fan-out", "order": 99, "config": { } }
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

### Load balancing

`cluster.loadBalancingPolicy` names a YARP load-balancing policy. Built in:

| Policy | Behaviour |
| ------ | --------- |
| `RoundRobin` (default) | Cycle through nodes in order |
| `Random` | Pick a node at random |
| `PowerOfTwoChoices` | Pick two at random, take the less busy. Random's throughput without its worst case |
| `LeastRequests` | Fewest in-flight requests; examines every node |
| `FirstAlphabetical` | Alphabetically first healthy node; dual-node failover |

A policy is a drop-in seam: implement YARP's `ILoadBalancingPolicy`, drop the DLL in `plugins/`,
name it in `cluster.loadBalancingPolicy`. The field is a free string rather than a closed enum so a
custom policy is nameable.

An unregistered name fails the gateway at **startup** and rejects an admin reload, with an error
naming the offending route and listing the available policies. In C#, the `LoadBalancingPolicy` enum
makes a typo a compile error:

```csharp
Cluster = new ClusterConfig
{
    LoadBalancingPolicy = LoadBalancingPolicy.LeastRequests.ToString(),
    Destinations = new Dictionary<string, DestinationConfig>
    {
        ["node-0"] = new() { Address = "http://svc:8080" },
    },
}
```

### Retries and circuit breaking

Retries are ConduitSharp's, not YARP's: a proxy cannot safely replay a half-streamed body, so the
gateway buffers the request, wraps the forwarder in a retry loop, and re-runs load balancing on each
attempt so a retry lands on a *different* node. Buffering is eager by default (subject to
`Gateway:RequestLimits`) so plugins always get a seekable stream.

`"streamOnly": true` opts a route out of buffering and streams straight to YARP with zero
allocations. Use it for large uploads no plugin needs to inspect. `streamOnly` cannot combine with
`retry`, which needs a rewindable body; both constraints are enforced **at startup**.

A plugin needing the *whole* request body (hashing, signature validation, audit capture) declares
`ReadsRequestBody => true` on its `IPipelinePlugin`. Such a plugin on a `streamOnly` route is
rejected at startup: with no buffered body it would consume YARP's forward-only stream and leave the
upstream a zero-length payload. Read through the buffered stream the gateway already provides
(`context.Request.Body` is seekable; rewind with `Position = 0`). Never call
`Request.EnableBuffering()` yourself, which buffers a second copy *outside* the gateway's memory
budget.

`ReadsRequestBody` costs the route its streaming path: the gateway buffers every request (memory,
then temp-file spill) to hand the plugin a rewindable stream. Most payload-inspecting plugins need
only a bounded prefix (logging, sampling, sniffing a content type). Leave `ReadsRequestBody => false`
and wrap `context.Request.Body` before calling `next` to observe bytes as they stream past; the route
keeps streaming. ASP.NET Core's `HttpLogging` middleware does exactly this. See
[plugins/ConduitSharp.Plugin.BodyCapture](../plugins/ConduitSharp.Plugin.BodyCapture) for both
patterns side by side.

Retries apply to **idempotent methods only** (`GET`, `HEAD`, `OPTIONS`, `PUT`, `DELETE`, `TRACE`) by
default; `POST`/`PATCH` never retries, having possibly already been applied upstream.
`"retry": { "maxAttempts": 3, "retryNonIdempotent": true }` opts a non-idempotent method in. A
replayed `POST` can **double-apply** if the first attempt reached the upstream, so enable it only
where that is harmless. Opting in forces the route to buffer. A retried attempt never reaches the
client; its response is held back and discarded.

Retry is a sibling of `cluster`, not part of it. YARP's `ClusterConfig` has no retry field, and its
`metadata` (a string-to-string dictionary) cannot hold a structured policy:

```json
"cluster": {
  "destinations": { "node-0": { "address": "http://user-service-1:8080" } },
  "httpRequest": { "activityTimeout": "00:00:05" }
},
"retry": {
  "maxAttempts": 3,
  "delayMs":     200,
  "backoff":     "Exponential",
  "jitter":      true,
  "retryOn":     [502, 503, 504]
}
```

| Field | Default | Meaning |
| ----- | ------- | ------- |
| `maxAttempts` | `1` | Total attempts including the first. `1` disables retries |
| `delayMs` | `0` | Base delay between attempts |
| `backoff` | `Fixed` | `Fixed`, `Linear`, or `Exponential` growth of that delay |
| `jitter` | `false` | Randomize each delay against a client stampede |
| `retryOn` | `[502, 503, 504]` | Upstream statuses that trigger a retry |

A connection failure or timeout always retries, whatever `retryOn` says. Omit `retry` and the route
does not retry.

The circuit breaker is likewise a sibling block:

```json
"circuitBreaker": { "threshold": 5, "cooldownMs": 10000 }
```

`threshold` consecutive failures against one node open its circuit and the load balancer stops
sending it traffic for `cooldownMs`; one trial request after the cooldown decides whether it recovers
or opens again. Omit the block, or set `threshold` to `0`, to disable it for a route. A client
disconnecting mid-request never counts as a node failure.

This is a YARP `IPassiveHealthCheckPolicy`. YARP's own passive policy is rate-over-a-window and
cannot express a consecutive-failure threshold, so ConduitSharp supplies one.

### Path syntax

| Pattern           | Example                    | Behaviour                                       |
| ----------------- | -------------------------- | ----------------------------------------------- |
| Literal           | `/api/orders`              | Exact segment match, case-insensitive           |
| Named parameter   | `/api/orders/{id}`         | Captures one segment                            |
| Catch-all         | `/api/users/{**rest}`      | Captures zero or more remaining segments        |

> Routes are evaluated **top-to-bottom, first match wins.** Place more specific routes before broader catch-alls.

### Query parameter matching

A `queryParams` block on `match` requires specific key=value pairs before a route is selected. All listed params must be present with the exact value; extra params on the request are ignored.

```json
{
  "match": {
    "path": "/search",
    "queryParams": { "version": "2", "format": "json" }
  }
}
```

| Request URL | Matches? |
|---|---|
| `/search?version=2&format=json` | Yes |
| `/search?version=2&format=json&page=1` | Yes, extra params ignored |
| `/search?version=2` | No, `format` missing |
| `/search?version=1&format=json` | No, `version` wrong value |

Omit the block or leave it empty (`{}`) to match any query string. The full original query string is always forwarded upstream unchanged: `queryParams` is a filter, not a transform.

### Per-route request body limit

`"maxRequestBodyBytes": <n>` at the top level of a route entry (alongside `id`, `match`, `upstream`)
overrides the global `Gateway.RequestLimits.MaxRequestBodyBytes`
(see [Gateway settings](GATEWAY_SETTINGS.md)) for that route: an upload endpoint needing a larger cap
than the rest of the gateway. Omit it, or leave it `null`, to inherit the global limit.

### Built-in plugins

| Name                  | What it does                                                         |
| --------------------- | -------------------------------------------------------------------- |
| `jwt-auth`            | Validates HS256 Bearer JWTs; enforces exp, nbf, iss, aud claims, and optional claim-based RBAC (`requiredClaims`) |
| `jwks-jwt-auth`       | Validates RS/ES Bearer JWTs via a remote JWKS endpoint (Auth0, Azure AD, Google, Keycloak); same optional `requiredClaims` RBAC |
| `api-key-auth`        | Validates API keys from a request header (plain-text comparison)     |
| `api-key-auth-hashed` | Validates API keys by comparing SHA-256 hash; keys never stored raw  |
| `rate-limit`          | Fixed-window quota enforcement per route or per client header value  |
| `cache`               | Response caching with configurable TTL and vary-by-header rules      |
| `header-transform`    | Add, remove, or rewrite request headers (upstream) and response headers (client) |
| `http-proxy`          | Marks where in the chain YARP forwards upstream. Declare it to place the forward explicitly; omit it and the forward is appended at the end |

---


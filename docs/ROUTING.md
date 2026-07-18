# Configuring routes

_Part of the [ConduitSharp documentation](../README.md)._


All routing lives in `Configuration/routes.json` (next to the binary). No database, no admin UI — just a file you can commit, review, and diff.

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

A route has two halves, and the split is deliberate:

- **`route` and `cluster` are YARP's own `RouteConfig` and `ClusterConfig`**, used verbatim. Nothing
  is projected or re-modelled, so *every* YARP feature is available the day YARP ships it — session
  affinity, active health checks, request/response transforms, host matching, `sslProtocols`,
  per-destination `host` overrides. Header and query matching get YARP's full matcher objects
  (`ExactHeader`, `Prefix`, `Contains`, `NotExists`, …), not just exact string equality.
  `routeId`, `clusterId` and `order` are derived from the route's `id` and its position in the file,
  so you never type them.
- **Everything else is ConduitSharp's** — `retry`, `circuitBreaker`, `plugins`, `swagger`,
  `maxRequestBodyBytes` — because YARP has no concept of any of them.

Write it all in camelCase; YARP's records bind case-insensitively.

> **Upgrading from 0.1.4?** The schema changed. See the
> [migration guide](../CHANGELOG.md#migrating-routesjson).

### Load balancing

`cluster.loadBalancingPolicy` names a YARP load-balancing policy. Built in:

| Policy | Behaviour |
| ------ | --------- |
| `RoundRobin` (default) | Cycle through nodes in order |
| `Random` | Pick a node at random |
| `PowerOfTwoChoices` | Pick two at random, take the less busy — Random's throughput, without its worst case |
| `LeastRequests` | Fewest in-flight requests; examines every node |
| `FirstAlphabetical` | Always the alphabetically first healthy node — dual-node failover |

A policy is a drop-in seam like any other: implement YARP's `ILoadBalancingPolicy`, drop the DLL in
`plugins/`, and name it in `cluster.loadBalancingPolicy`. That is why the field is a free string
rather than a closed enum — a custom policy has to be nameable.

An unregistered name fails the gateway at **startup** (and rejects an admin reload), with an error
that names the offending route and lists the policies actually available. Building routes in C#?
Use the `LoadBalancingPolicy` enum for the built-ins so a typo is a compile error instead:

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

Retries are ConduitSharp's, not YARP's — a proxy cannot safely replay a half-streamed body, so the
gateway buffers the request, wraps the forwarder in a retry loop, and re-runs load balancing on
each attempt so a retry lands on a *different* node. By default, ConduitSharp eagerly buffers all
requests (subject to `Gateway:RequestLimits`) to ensure plugins always have access to a seekable stream.

If you are uploading large files and know that no plugins need to inspect the body, you can opt out
of buffering by setting `"streamOnly": true` on the route. This streams the request directly to YARP
with zero allocations. `streamOnly` cannot be combined with `retry` (a retry needs a rewindable
body), and both constraints are enforced **at startup**, not at request time.

A plugin that needs the *whole* request body — to hash it, validate a signature, capture it for audit —
declares `ReadsRequestBody => true` on its `IPipelinePlugin` implementation. Putting such a plugin
on a `streamOnly` route is rejected at startup: without the buffered body it would consume YARP's
forward-only stream and leave the upstream a zero-length payload. Read the body through the buffered
stream the gateway already provides (`context.Request.Body` is seekable — rewind with `Position = 0`);
never call `Request.EnableBuffering()` yourself, which buffers a second copy *outside* the gateway's
memory budget.

Most payload-inspecting plugins don't need the whole body, and declaring `ReadsRequestBody` costs the
route its streaming path — the gateway buffers every request (memory, then temp-file spill) just to
hand the plugin a rewindable stream. If a bounded prefix is enough (logging, sampling, sniffing a
content type), leave `ReadsRequestBody => false` and observe the bytes as they stream past instead:
wrap `context.Request.Body` before calling `next`, and the route keeps streaming. ASP.NET Core's own
`HttpLogging` middleware does exactly this and is worth reaching for before writing your own wrapper.
See [examples/ConduitSharp.Plugin.BodyCapture](../examples/ConduitSharp.Plugin.BodyCapture) for both
patterns side by side.

Retries apply to **idempotent methods only** (`GET`, `HEAD`, `OPTIONS`, `PUT`, `DELETE`, `TRACE`);
a `POST`/`PATCH` never retries, since it may already have been applied upstream. A retried attempt
never reaches the client — its response is held back and discarded.

Retry is a sibling of `cluster`, not part of it — YARP's `ClusterConfig` has no retry field, and its
`metadata` (a string-to-string dictionary) could never hold a structured policy:

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
| `jitter` | `false` | Randomize each delay, so retries from many clients do not stampede |
| `retryOn` | `[502, 503, 504]` | Upstream statuses that trigger a retry |

A connection failure or timeout always retries, whatever `retryOn` says. Omit `retry` entirely and
the route does not retry.

The circuit breaker is likewise a sibling block:

```json
"circuitBreaker": { "threshold": 5, "cooldownMs": 10000 }
```

`threshold` consecutive failures against one node open its circuit, so the load balancer stops
sending it traffic for `cooldownMs`; one trial request after the cooldown decides whether it
recovers or opens again. Omit the block (or set `threshold` to `0`) to disable circuit breaking for
a route. A client disconnecting mid-request is never counted as a node failure.

Under the hood this is a YARP `IPassiveHealthCheckPolicy` — YARP's own passive policy is
rate-over-a-window and cannot express a consecutive-failure threshold, so ConduitSharp supplies one.

### Path syntax

| Pattern           | Example                    | Behaviour                                       |
| ----------------- | -------------------------- | ----------------------------------------------- |
| Literal           | `/api/orders`              | Exact segment match, case-insensitive           |
| Named parameter   | `/api/orders/{id}`         | Captures one segment                            |
| Catch-all         | `/api/users/{**rest}`      | Captures zero or more remaining segments        |

> Routes are evaluated **top-to-bottom, first match wins.** Place more specific routes before broader catch-alls.

### Query parameter matching

Add a `queryParams` block to `match` to require specific key=value pairs before a route is selected. All listed params must be present with the exact value — extra params on the request are ignored.

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
| `/search?version=2&format=json&page=1` | Yes — extra params ignored |
| `/search?version=2` | No — `format` missing |
| `/search?version=1&format=json` | No — `version` wrong value |

Omit the block or leave it empty (`{}`) to match any query string. The full original query string is always forwarded to the upstream unchanged — `queryParams` in `match` is a filter only, not a transform.

### Per-route request body limit

Add `"maxRequestBodyBytes": <n>` at the top level of a route entry (alongside `id`,
`match`, `upstream`) to override the global `Gateway.RequestLimits.MaxRequestBodyBytes`
(see [Gateway settings](GATEWAY_SETTINGS.md)) for just that route — useful for an upload
endpoint that legitimately needs a larger cap than the rest of the gateway. Omit it, or
leave it `null`, to inherit the global limit.

### Built-in plugins

| Name                  | What it does                                                         |
| --------------------- | -------------------------------------------------------------------- |
| `jwt-auth`            | Validates HS256 Bearer JWTs; enforces exp, nbf, iss, aud claims, and optional claim-based RBAC (`requiredClaims`) |
| `jwks-jwt-auth`       | Validates RS/ES Bearer JWTs via a remote JWKS endpoint (Auth0, Azure AD, Google, Keycloak); same optional `requiredClaims` RBAC |
| `api-key-auth`        | Validates API keys from a request header (plain-text comparison)     |
| `api-key-auth-hashed` | Validates API keys by comparing SHA-256 hash; keys never stored raw  |
| `rate-limit`          | Fixed-window quota enforcement per route or per client header value  |
| `cache`               | Response caching with configurable TTL and vary-by-header rules      |
| `header-transform`    | Add, remove, or rewrite request headers before forwarding upstream   |
| `http-proxy`          | Not a plugin — names where in the chain YARP forwards to the upstream. Declare it to place the forward explicitly, or omit it and the forward is appended at the end of the chain |

---


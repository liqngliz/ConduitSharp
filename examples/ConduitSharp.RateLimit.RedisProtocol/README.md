# ConduitSharp.RateLimit.RedisProtocol

A drop-in **distributed rate-limit store** for ConduitSharp over the **Redis protocol (RESP)**.
Works with **Valkey**, **Redis 7**, and any RESP-compatible server (KeyDB, Dragonfly, AWS
ElastiCache/MemoryDB). All gateway instances share one fixed-window counter, so a client's
quota holds across every replica instead of multiplying per instance.

It replaces the built-in in-process `InMemoryRateLimitStore` with no core changes: the gateway
discovers an `IRateLimitStore` implementation dropped into its plugins root (last-registration-wins).

- **Shared across instances** — one quota, however many replicas serve the route.
- **Fails open** — if the store is unreachable, requests pass rather than being rejected, and
  the gateway starts even when the backend is briefly down.

## Install

1. **Build the drop-in:**

   ```bash
   dotnet publish examples/ConduitSharp.RateLimit.RedisProtocol/src/ConduitSharp.RateLimit.RedisProtocol \
     -c Release -o out/redis-ratelimit
   ```

2. **Drop it into the gateway's plugins root** (the top level, not a per-route subdirectory), or reference the `ConduitSharp.RateLimit.RedisProtocol` package and register it in DI when embedding the gateway.

3. **Configure the connection** (appsettings.json or environment variables):

   ```json
   {
     "Gateway": {
       "RateLimiting": {
         "Redis": {
           "ConnectionString": "redis:6379"
         }
       }
     }
   }
   ```

### Options (`Gateway:RateLimiting:Redis`)

| Setting | Default | Description |
| :--- | :--- | :--- |
| `ConnectionString` | *(required)* | RESP server, e.g. `valkey:6379` |
| `KeyPrefix` | `conduitsharp:ratelimit:` | Prefix for all counter keys |
| `Database` | `-1` | Redis logical database (`-1` = server default) |

Routes keep their normal `rate-limit` plugin config (`windowSeconds`, `maxRequests`) — only the counter storage moves to the shared backend.

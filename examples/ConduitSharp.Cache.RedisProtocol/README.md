# ConduitSharp.Cache.RedisProtocol

A drop-in **distributed response cache** for ConduitSharp over the **Redis protocol (RESP)**.
Works with **Valkey**, **Redis 7**, and any RESP-compatible server (KeyDB, Dragonfly, AWS
ElastiCache/MemoryDB) — same code, just point it at your server. All gateway instances share
one cache, so a response cached by any instance is served by every instance.

> **Which backend?** [Valkey](https://valkey.io) is the BSD-licensed, Linux-Foundation fork
> of Redis 7.2 and the recommended open-source default (Redis 7.4+ moved to the
> source-available RSALv2/SSPL). Redis 7 (the last BSD line) also works. The drop-in is
> verified against both.

- **Shared across instances** — no more per-replica cache duplication.
- **Stampede protection** — concurrent misses for the same key collapse onto a single
  upstream fetch (request coalescing).
- **Route invalidation** — `DELETE /admin/cache/{routeId}` flushes a route's cached responses.
- **Fails open** — if the cache server is unreachable the gateway degrades to no-caching
  (misses go to the upstream) rather than failing requests, and starts even when it's briefly down.

It replaces the built-in in-process `InMemoryCacheService` with no core changes: the gateway
discovers a cache backend dropped into its plugins root.

## Install

1. **Build the drop-in:**

   ```bash
   dotnet publish examples/ConduitSharp.Cache.RedisProtocol/src/ConduitSharp.Cache.RedisProtocol \
     -c Release -o out/redis-cache
   ```

2. **Drop it into the gateway's plugins root** (the top level, not a per-route subdirectory):

   ```bash
   cp out/redis-cache/ConduitSharp.Cache.RedisProtocol.dll out/redis-cache/StackExchange.Redis.dll \
      "$GATEWAY_PLUGINS_DIR/"
   ```

   The gateway scans the plugins root for an `ICacheService` implementation and uses it in
   place of the built-in cache (last-registration-wins).

3. **Configure the connection** (appsettings.json or environment variables):

   ```json
   {
     "Gateway": {
       "Cache": {
         "Redis": {
           "ConnectionString": "redis:6379",
           "KeyPrefix": "conduitsharp:cache:",
           "Database": -1
         }
       }
     }
   }
   ```

   Or via env vars: `Gateway__Cache__Redis__ConnectionString=redis:6379`.

That's it — routes that use the `cache` plugin now cache to Redis.

## Invalidation

Flush every cached response for a route (requires `Gateway:AdminKeyHash` to be configured):

```bash
curl -X DELETE http://gateway:5050/admin/cache/<routeId> -H "X-Admin-Key: <secret>"
# → Invalidated 3 cache entries for route '<routeId>'.
```

## Config reference

| Key | Default | Meaning |
|-----|---------|---------|
| `Gateway:Cache:Redis:ConnectionString` | — (required) | StackExchange.Redis connection string. |
| `Gateway:Cache:Redis:KeyPrefix` | `conduitsharp:cache:` | Prefix isolating this gateway's keys. |
| `Gateway:Cache:Redis:Database` | `-1` | Redis database index (`-1` = the connection default). |

## Notes

- **Stampede protection** engages when the response-producing plugin (`http-proxy` or a
  terminal plugin) is declared *in* the route's plugin chain after `cache`. If `http-proxy`
  is left to the implicit fallback, each request fetches independently (still correct).
- Cached bodies are serialized as JSON; the cache is intended for text responses (the
  built-in cache has the same assumption).

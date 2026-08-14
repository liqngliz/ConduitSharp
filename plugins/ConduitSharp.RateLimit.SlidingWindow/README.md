# ConduitSharp.RateLimit.SlidingWindow

A drop-in **rate-limit algorithm** (`IRateLimiter`) that replaces ConduitSharp's built-in
fixed-window limiter with a sliding log.

## Why

Fixed windows are cheap but bursty at the seam. With `maxRequests: 5, windowSeconds: 60`, a caller
can spend all 5 at 11:00:59 and another 5 at 11:01:00 — **10 requests in one second**, twice the
nominal rate, entirely within the rules. The window resets on an aligned boundary, and the boundary
does not care how recently you spent your quota.

A sliding log keeps the timestamp of every permit still inside the window, so the limit holds at
*every* instant rather than only the aligned ones. The [tests](tests/) assert exactly this — the
same burst, refused here and allowed by the fixed window.

The trade is memory: **O(maxRequests) timestamps per active key** against the fixed window's single
counter. Worth it for expensive endpoints where the burst is what you are paying for; not worth it
for a coarse per-minute quota over cheap reads.

## Use it

Build and drop the DLL in the gateway's `plugins/` directory. Discovery is automatic — the same
seam `ConduitSharp.RateLimit.RedisProtocol` uses, and the last registration wins:

```bash
dotnet build -c Release
cp src/ConduitSharp.RateLimit.SlidingWindow/bin/Release/net10.0/ConduitSharp.RateLimit.SlidingWindow.dll \
   /path/to/gateway/plugins/
```

No routes.json change. The `rate-limit` plugin keeps its existing config — `windowSeconds`,
`maxRequests`, `keyHeader` all mean the same thing; only *when* a permit frees changes:

```json
{
  "name": "rate-limit",
  "config": { "windowSeconds": 60, "maxRequests": 5, "keyHeader": "X-Api-Key" }
}
```

`Retry-After` follows the algorithm automatically: the fixed window answers "seconds to the next
boundary", this answers "seconds until your oldest request ages out".

## Two seams, not one

| | Interface | Change it to… |
|---|---|---|
| **Algorithm** | `IRateLimiter` | alter what "over the limit" means (this package) |
| **Counter backend** | `IRateLimitStore` | share counters across replicas (`RateLimit.RedisProtocol`) |

They are independent because the reasons to change them are. Note this package does **not** use
`IRateLimitStore`: that interface counts hits per aligned window id, which is precisely the model a
sliding log rejects. An algorithm is free to keep state no store can express — which is why the two
are separate seams rather than one.

The consequence: **state here is per-process, so quotas are per replica.** Behind a load balancer,
N replicas mean up to N× the quota. A distributed sliding log needs a backend that can hold a
per-key timestamp log (e.g. a Redis sorted set) — a worthwhile extension, and not what this example
is for.

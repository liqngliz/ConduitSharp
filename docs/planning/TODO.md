# Planning TODO

## Docs — additional examples

- [x] **Entra JWKS token authorization example — JSON config for both security groups AND app roles.** _Done — added to README → Configuring routes → "Authorizing with Microsoft Entra ID". Detail retained below as reference._ Document how to gate a route on Microsoft Entra (Azure AD) using the existing `jwks-jwt-auth` plugin — no code change needed, `requiredClaims` + `anyOf`/`allOf` already covers both. Add as sample route JSON and/or an Entra block in the plugin's XML doc. Shared config for both: `jwksUri` = `https://login.microsoftonline.com/{tenantId}/discovery/v2.0/keys`, `issuer` = `https://login.microsoftonline.com/{tenantId}/v2.0`, `audience` = API app client id.

  **A) Security groups** — gate on `groups` claim:
  - Config: `requiredClaims: [{ "claim": "groups", "anyOf": ["<group-object-id-guid>", ...] }]`.
  - Entra side: set `"groupMembershipClaims": "SecurityGroup"` in the app manifest so `groups` is emitted (array of group **object-id GUIDs**, not names). On-prem/Windows AD groups synced via Entra Connect appear as their Entra object IDs.
  - Gotchas: (1) **groups overage** — >200 groups drops the `groups` claim entirely and emits `_claim_names`/`_claim_sources` (Graph pointer) the plugin can't resolve; recommend app roles (below) or `"ApplicationGroup"` instead. (2) **v1 vs v2** issuer/audience mismatch → 401.

  **B) App roles** — gate on `roles` claim (preferred — no overage, readable values):
  - Config: `requiredClaims: [{ "claim": "roles", "anyOf": ["Admin", "Read"] }]`.
  - Entra side: define `appRoles` in the API app registration manifest, then assign users/groups to them in the Enterprise App. The `roles` claim is emitted **by default** for the app (no `groupMembershipClaims`-style toggle) as an array of role **value strings** (e.g. `"Admin"`), not GUIDs.
  - Advantage over groups: stable readable values, no 200-role overage, decoupled from directory group sprawl.

## Release 1.0.0 (branch `release/1.0.0`, from `1.0.0-rc.1`)

Promoting the RC to GA. Done on the branch:
- [x] `Directory.Build.props` `<Version>` → `1.0.0` (single source of truth; `release.yml` `verify-version` checks the tag matches it).
- [x] README — dropped `--prerelease` from both install snippets.
- [x] CHANGELOG — `[1.0.0]` section dated 2026-07-23.

Still to do:
- [ ] **Merge `release/1.0.0` to `main`, THEN tag `v1.0.0`.** The tag (not the branch) triggers `release.yml` — publishes NuGet, GHCR image, and binaries. Do not tag from the branch pre-merge.
- [x] **NuGet publish gap** — `release.yml` now packs/pushes all 5 advertised example plugins (added `ConduitSharp.Plugin.PowerShell` and `ConduitSharp.RateLimit.SlidingWindow` to the pack list). `BodyCaptureToFile` stays `IsPackable=false` (bench-only).
- [ ] **nuget.org Trusted Publisher — manual, before tagging.** The two newly-added package IDs need a one-time Trusted Publishing entry on nuget.org or the `dotnet nuget push` 403s: for **`ConduitSharp.Plugin.PowerShell`** and **`ConduitSharp.RateLimit.SlidingWindow`**, add repo `liqngliz/ConduitSharp` + workflow `release.yml` (Profile → Trusted Publishing, per package ID).

## Post-1.0 features

- [ ] **Distributed sliding-window rate limiter** (new package, e.g. `ConduitSharp.RateLimit.RedisSlidingWindow`). Fills the missing quadrant: today you get *distributed but bursty* (Redis fixed-window store) OR *accurate but local* (in-memory `SlidingWindow` algorithm), never both — because `SlidingWindowRateLimiter` keeps its log in-memory and ignores `IRateLimitStore`, and the store contract is fixed-window-shaped (a `windowId` bucket, no timestamps).
  - Implement **`IRateLimiter`** (the algorithm seam), NOT `IRateLimitStore` — the algorithm is free to hold state in Redis directly (StackExchange.Redis + a Lua script for atomicity).
  - Two options: **(a) sorted-set sliding log** — per-key ZSET, `ZREMRANGEBYSCORE` aged-out + `ZCARD` + `ZADD`, exact; trade is O(max) entries/key in Redis. **(b) sliding-window-counter** — current+previous fixed-window counts weighted by overlap (Cloudflare's method), O(1) memory, near-exact.
  - Result: distributed AND accurate (no 2× boundary burst). Mirrors the existing Redis store package's config seam (`Gateway:RateLimiting:Redis:*`, fail-open on backend outage).

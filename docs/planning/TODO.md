# Planning TODO

## Docs — additional examples

- [ ] **Entra security-group authorization example.** Document how to gate a route on Microsoft Entra (Azure AD) security-group membership using the existing `jwks-jwt-auth` plugin — no code change needed, `requiredClaims` + `anyOf` on the `groups` claim already covers it. Add as a sample route JSON and/or an Entra block in the plugin's XML doc.
  - Config: `jwksUri` = `https://login.microsoftonline.com/{tenantId}/discovery/v2.0/keys`, `issuer` = `https://login.microsoftonline.com/{tenantId}/v2.0`, `audience` = API app client id; `requiredClaims: [{ "claim": "groups", "anyOf": ["<group-object-id-guid>", ...] }]`.
  - Entra side: set `"groupMembershipClaims": "SecurityGroup"` in the app manifest so `groups` is emitted (array of group **object-id GUIDs**, not names). On-prem/Windows AD groups synced via Entra Connect appear as their Entra object IDs.
  - Must document two gotchas: (1) **groups overage** — >200 groups drops the `groups` claim entirely and emits `_claim_names`/`_claim_sources` (Graph pointer) which the plugin cannot resolve; recommend `"ApplicationGroup"` or app roles instead. (2) **v1 vs v2** issuer/audience mismatch → 401.

## Release 1.0.0 (branch `release/1.0.0`, from `1.0.0-rc.1`)

Promoting the RC to GA. Done on the branch:
- [x] `Directory.Build.props` `<Version>` → `1.0.0` (single source of truth; `release.yml` `verify-version` checks the tag matches it).
- [x] README — dropped `--prerelease` from both install snippets.
- [x] CHANGELOG — `[1.0.0]` section dated 2026-07-23.

Still to do:
- [ ] **Merge `release/1.0.0` to `main`, THEN tag `v1.0.0`.** The tag (not the branch) triggers `release.yml` — publishes NuGet, GHCR image, and binaries. Do not tag from the branch pre-merge.
- [ ] **NuGet publish gap — fix before tagging.** `release.yml` pushes only 3 example plugins (BodyCapture, Cache.RedisProtocol, RateLimit.RedisProtocol), but the README shipped-plugins table advertises NuGet packages for **5** — also PowerShell and SlidingWindow. Both are `IsPackable=true` with a `PackageId` but are **missing from the `dotnet pack` list in `release.yml`**. Either add them to the publish list (+ nuget.org Trusted Publisher setup per new PackageId) or drop the NuGet column for them in the README. `BodyCaptureToFile` is correctly `IsPackable=false` (bench-only), no action.

# Claim-based authorization (RBAC)

_Part of the [ConduitSharp documentation](../README.md)._


`jwt-auth` and `jwks-jwt-auth` validate a token's signature, expiry, issuer, and audience —
but a *valid* token doesn't mean the caller is *allowed* to hit this particular route. Add
a `"requiredClaims"` array to either plugin's config to enforce that too — Entra app roles,
Auth0 namespaced roles, Okta/Entra scopes, Keycloak realm roles, or any other claim your
identity provider issues:

```json
{
  "name": "jwks-jwt-auth",
  "order": 1,
  "config": {
    "jwksUri": "https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys",
    "issuer":  "...",
    "audience": "...",
    "requiredClaims": [
      { "claim": "roles", "anyOf": ["Finance.Reader", "Finance.Admin"] },
      { "claim": "scp", "allOf": ["reports.read"], "delimiter": " " },
      { "claim": "realm_access.roles", "anyOf": ["finance"] },
      { "claim": "https://example.com/roles", "anyOf": ["admin"] },
      { "claim": "email_verified", "equals": "true" },
      { "claim": "hd" }
    ]
  }
}
```

A missing or non-matching claim short-circuits **403 Forbidden** — not 401 — because the
token itself is valid; the caller just lacks permission for this route. All entries in
`requiredClaims` must pass (logical AND).

Each rule names a `claim` plus at most one matcher:

| Matcher | Semantics |
|---|---|
| *(none)* | The claim must merely exist, with any value |
| `equals` | The claim's value must equal this exactly |
| `anyOf`  | The claim's value set must intersect this list — the typical "one of these roles" check |
| `allOf`  | The claim's value set must contain every entry in this list — typical for OAuth scopes |

The claim's value becomes a set before matching: a JSON array becomes the set of its
members (Entra app roles: `"roles": ["Finance.Admin"]`); a single string becomes a
one-element set, unless `"delimiter"` is set, which splits it first (Entra/Okta's
space-delimited `scp`/`scope`: `"scp": "reports.read reports.write"`); a boolean or number
becomes its string form (Google's `"email_verified": true`).

**Claim lookup** tries the literal top-level property name first — so a namespaced claim
that itself contains dots, like Auth0's `https://example.com/roles`, matches directly — and
only falls back to splitting the name on `.` and traversing into nested objects if no
literal match exists, which is how Keycloak's `realm_access.roles` resolves.

A malformed `requiredClaims` block (an empty claim name, an empty `anyOf`/`allOf`, or more
than one matcher on the same rule) fails at startup — via the same fail-fast path as an
invalid `rate-limit` or `jwks-jwt-auth` config — rather than on the first request.

### Multiple JWT providers per route

To allow multiple identity providers (e.g., accepting tokens from either Auth0 *or* Azure AD on the same endpoint), you can pass an array of providers to the `"providers"` key in either `jwt-auth` or `jwks-jwt-auth`.

The plugin will evaluate the token against each provider sequentially. If *any* provider successfully validates the token (logical OR), the request is allowed through. Each provider can also declare its own distinct `requiredClaims`.

**routes.json:**
```json
{
  "name": "jwks-jwt-auth",
  "order": 1,
  "config": {
    "providers": [
      {
        "jwksUri": "https://your-tenant.auth0.com/.well-known/jwks.json",
        "issuer": "https://your-tenant.auth0.com/",
        "requiredClaims": [ { "claim": "https://example.com/roles", "anyOf": ["admin"] } ]
      },
      {
        "jwksUri": "https://login.microsoftonline.com/<tenant-id>/discovery/v2.0/keys",
        "issuer": "https://login.microsoftonline.com/<tenant-id>/v2.0",
        "requiredClaims": [ { "claim": "roles", "anyOf": ["finance.user"] } ]
      }
    ]
  }
}
```

### Microsoft Entra ID (Azure AD) — v2.0 token, app-role RBAC

Locking a route to a single Entra app role (`finance.user`) end to end:

```jsonc
{
  "id": "finance-api-route",
  "route": { "match": { "path": "/api/finance/{**catch-all}", "methods": ["GET", "POST", "PUT", "DELETE"] } },
  "cluster": {
    "loadBalancingPolicy": "RoundRobin",
    "destinations": { "node-0": { "address": "https://my-backend.example.com" } },
    "httpRequest": { "activityTimeout": "00:00:10" }
  },
  "plugins": [
    {
      "name": "jwks-jwt-auth",
      "order": 1,
      "enabled": true,
      "config": {
        "jwksUri":  "https://login.microsoftonline.com/<tenant-id>/discovery/v2.0/keys",
        "issuer":   "https://login.microsoftonline.com/<tenant-id>/v2.0",
        "audience": "<api-client-id-guid>",
        "requiredClaims": [
          { "claim": "roles", "anyOf": ["finance.user"] }
        ]
      }
    }
  ]
}
```

`issuer`/`audience` here are the **v2.0** pairing. This only matches tokens from an API app
registration whose manifest has `"accessTokenAcceptedVersion": 2` — the (more common,
unset-by-default) v1.0 pairing is `issuer: "https://sts.windows.net/<tenant-id>/"` and
`audience: "api://<api-app-id-uri>"` instead. Pick whichever matches what your API's app
registration actually issues — decode a real token at [jwt.ms](https://jwt.ms) and read its
`iss`/`aud` rather than guessing; the two pairings never appear in the same token, and a
mismatch 401s with `"Invalid issuer."` or `"Invalid audience."`.

**Getting a token that actually carries the `roles` claim** (Entra portal steps, one-time setup):

1. **Define the app role** on the *API's* app registration → **App roles** → **Create app role**.
   Set **Value** to `finance.user` exactly — this is a case-sensitive string, and it's what
   ends up in the token's `roles` array. Allowed member types: Users/Groups (or
   Applications, for service-to-service calls).
2. **Assign the role** — API's app registration → **Enterprise applications** → find the
   same app → **Users and groups** → **Add assignment** → pick the user/group → select the
   `finance.user` role. Without this step the token is still valid, it just won't carry
   `roles` at all (see below).
3. **Client requests a token for this API's scope** — e.g.
   `az account get-access-token --resource api://<api-app-id-uri>` for a quick manual test,
   or an OAuth client-credentials/auth-code flow requesting
   `api://<api-app-id-uri>/.default` in production. The returned access token's payload now
   includes `"roles": ["finance.user"]`.
4. **Verify** — paste the token into [jwt.ms](https://jwt.ms) and confirm `iss`, `aud`, and
   `roles` all match what's in the route config above.

Two failure modes this produces, both intentional:

- **Unassigned user, valid token** → `roles` claim is *absent from the token entirely*
  (Entra omits it, rather than sending an empty array) → gateway returns
  `403 Missing required claim 'roles'.` — the token is fine, the user just isn't
  provisioned for this route yet.
- **Wrong `issuer`/`audience` pairing** → `401 Invalid issuer.` / `401 Invalid audience.`
  before `requiredClaims` is ever evaluated — fix the config pairing above, not the role
  assignment.

---


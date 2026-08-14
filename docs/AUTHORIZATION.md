# Claim-based authorization (RBAC)

_Part of the [ConduitSharp documentation](../README.md)._


`jwt-auth` and `jwks-jwt-auth` validate signature, expiry, issuer, and audience. A
`"requiredClaims"` array adds per-route permission on top: Entra app roles, Auth0 namespaced
roles, Okta/Entra scopes, Keycloak realm roles, any claim the IdP issues.

```json
{
  "name": "jwks-jwt-auth",
  "order": 1,
  "config": {
    "jwksUri": "https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys",
    "issuer":  "...",
    "audience": "...",
    "requiredClaims": [
      { "claim": "roles", "anyOf": ["Read", "Admin"] },
      { "claim": "scp", "allOf": ["reports.read"], "delimiter": " " },
      { "claim": "realm_access.roles", "anyOf": ["erp"] },
      { "claim": "https://example.com/roles", "anyOf": ["admin"] },
      { "claim": "email_verified", "equals": "true" },
      { "claim": "hd" }
    ]
  }
}
```

A missing or non-matching claim short-circuits **403 Forbidden**, not 401: the token is valid,
the caller lacks permission. All entries in `requiredClaims` must pass (logical AND).

Each rule names a `claim` plus at most one matcher:

| Matcher | Semantics |
|---|---|
| *(none)* | Claim exists, any value |
| `equals` | Value equals this exactly |
| `anyOf`  | Value set intersects this list ("one of these roles") |
| `allOf`  | Value set contains every entry (typical for OAuth scopes) |

The value becomes a set before matching:

| Claim shape | Becomes | Example |
|---|---|---|
| JSON array | its members | Entra app roles, `"roles": ["Admin"]` |
| string | one-element set | |
| string + `"delimiter"` | split on the delimiter | Entra/Okta `"scp": "reports.read reports.write"` |
| bool / number | its string form | Google `"email_verified": true` |

**Claim lookup** tries the literal top-level property name first, so a namespaced claim
containing dots (Auth0's `https://example.com/roles`) matches directly. Only on no literal match
does it split the name on `.` and traverse nested objects, which is how Keycloak's
`realm_access.roles` resolves.

A malformed `requiredClaims` block (empty claim name, empty `anyOf`/`allOf`, or two matchers on
one rule) fails at startup, not on the first request.

### Multiple JWT providers per route

A `"providers"` array on `jwt-auth` or `jwks-jwt-auth` accepts tokens from several IdPs on one
endpoint (Auth0 *or* Azure AD). Providers are evaluated in order; the first that validates lets
the request through (logical OR). Each carries its own `requiredClaims`.

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
        "requiredClaims": [ { "claim": "roles", "anyOf": ["erp.user"] } ]
      }
    ]
  }
}
```

### Microsoft Entra ID (Azure AD): v2.0 token, app-role RBAC

Locking a route to a single Entra app role (`erp.user`) end to end:

```jsonc
{
  "id": "erp-api-route",
  "route": { "match": { "path": "/api/erp/{**catch-all}", "methods": ["GET", "POST", "PUT", "DELETE"] } },
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
          { "claim": "roles", "anyOf": ["erp.user"] }
        ]
      }
    }
  ]
}
```

Two mutually exclusive pairings, one per token. Match whichever your API's app registration
actually issues; decode a real token at [jwt.ms](https://jwt.ms) and read `iss`/`aud`.

| | `issuer` | `audience` | When |
|---|---|---|---|
| **v2.0** (above) | `https://login.microsoftonline.com/<tenant-id>/v2.0` | `<api-client-id-guid>` | manifest has `"accessTokenAcceptedVersion": 2` |
| **v1.0** | `https://sts.windows.net/<tenant-id>/` | `api://<api-app-id-uri>` | default, more common |

A mismatch 401s with `"Invalid issuer."` or `"Invalid audience."`.

**Getting a token that carries the `roles` claim** (Entra portal, one-time):

1. **Define the app role** on the *API's* app registration → **App roles** → **Create app role**.
   **Value** = `erp.user`, case-sensitive, lands verbatim in the token's `roles` array. Allowed
   member types: Users/Groups, or Applications for service-to-service.
2. **Assign the role**: API's app registration → **Enterprise applications** → same app →
   **Users and groups** → **Add assignment** → user/group → `erp.user`. Skip this and the token
   is still valid but carries no `roles` at all.
3. **Client requests a token for this API's scope**:
   `az account get-access-token --resource api://<api-app-id-uri>` for a manual test, or an OAuth
   client-credentials/auth-code flow requesting `api://<api-app-id-uri>/.default` in production.
   The payload now includes `"roles": ["erp.user"]`.
4. **Verify** at [jwt.ms](https://jwt.ms): `iss`, `aud`, `roles` match the route config above.

Two failure modes, both intentional:

| Cause | Result |
|---|---|
| Unassigned user, valid token | Entra omits `roles` entirely (not an empty array) → `403 Missing required claim 'roles'.` Provision the user; the token is fine. |
| Wrong `issuer`/`audience` pairing | `401 Invalid issuer.` / `401 Invalid audience.`, before `requiredClaims` runs. Fix the pairing, not the role assignment. |

---


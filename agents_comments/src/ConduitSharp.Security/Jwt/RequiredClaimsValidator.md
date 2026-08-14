# src/ConduitSharp.Security/Jwt/RequiredClaimsValidator.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RequiredClaimsValidator.ValidateOne

- (was line 34) No matcher configured — existence alone satisfies the rule.
- (was line 50) AllOf

## RequiredClaimsValidator.TryGetClaim

- (was line 56) Literal top-level name first — handles a namespaced claim name that itself contains dots (e.g. Auth0's "https://example.com/roles"). Only falls back to dot-path traversal (e.g. Keycloak's "realm_access.roles") when no literal match exists.

## RequiredClaimsValidator.ToStringSet

- (was line 78) A JSON array becomes the set of its members; a single string is one member, unless delimiter is set (splits a space-delimited OAuth scope claim); anything else (bool, number) becomes its invariant string form.

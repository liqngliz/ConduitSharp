# tests/ConduitSharp.Integration.Tests/Pipeline/Security/JwtAuthEndToEndTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwtAuthEndToEndTests.SignedTokenWithNonIntegerExp_Returns401_Not500

- (was line 59) Regression: exp.GetInt64() on a string exp threw InvalidOperationException, which surfaced as a 500 from the middleware's catch-all instead of a 401.

## JwtAuthEndToEndTests.Same_plugin_on_four_routes_keeps_separate_configs.Secret

- (was line 107) 32-byte secrets (HS256 minimum), one per letter.

## JwtAuthEndToEndTests.Same_plugin_on_four_routes_keeps_separate_configs

- (was line 111) Routes a/b: single signingKey. Routes c/d: providers lists with overlapping keys — a merge or overwrite of any route's config breaks a matrix cell.

## JwtAuthEndToEndTests.RoutesWithRequiredRole

- (was line 155) requiredClaims (RBAC) — a valid token lacking permission is 403, not 401

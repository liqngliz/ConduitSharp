# tests/ConduitSharp.Security.Tests/Jwt/JwksJwtAuthPluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwksJwtAuthPluginTests.ValidateConfig_MissingJwksUri_Throws

- (was line 29) Bearer extraction failures

## JwksJwtAuthPluginTests.ExecuteAsync_MultipleProviders_SucceedsIfAnyProviderValidates

- (was line 43) Multiple Providers
- (was line 49) Provider 1 fails signature, Provider 2 succeeds

## JwksJwtAuthPluginTests.ExecuteAsync_MultipleProviders_Returns403IfAnyProviderFailsRbac

- (was line 72) Provider 1 matches signature but fails RBAC (should yield 403) Provider 2 fails signature (should yield 401) Expected final result: 403 (Forbidden is higher precedence than Unauthorized)
- (was line 82) Token doesn't have this

## JwksJwtAuthPluginTests.ExecuteAsync_TokenSignedByWrongKey_ShortCircuits401

- (was line 122) Handler validation failure

## JwksJwtAuthPluginTests.ExecuteAsync_ValidToken_CallsNext

- (was line 152) Happy path

## JwksJwtAuthPluginTests.ExecuteAsync_ValidTokenMissingRequiredRole_ShortCircuits403

- (was line 169) requiredClaims (RBAC) — 403, not 401, on a valid token lacking permission

## JwksJwtAuthPluginTests.ExecuteAsync_InvalidTokenWithRequiredClaims_StillReturns401NotChecked

- (was line 207) A failed handler validation must short-circuit 401 before requiredClaims is ever evaluated — requiredClaims is present here but never reached.

## JwksJwtAuthPluginTests.ExecuteAsync_ReadsConfigFromContextPluginConfig

- (was line 223) Config comes from context.PluginConfig (routes.json round-trip)
- (was line 229) The audience constraint only exists in the JSON placed on the context — proving ExecuteAsync parses context.PluginConfig rather than any ambient default.

## JwksJwtAuthPluginTests.ExecuteAsync_SamePluginFourConfigs_KeysStayPerJwksUri

- (was line 241) Per-route config isolation: one plugin/handler/factory (the singleton wiring), four route configs with overlapping jwksUris — keys must stay per URI.
- (was line 269) key d valid nowhere

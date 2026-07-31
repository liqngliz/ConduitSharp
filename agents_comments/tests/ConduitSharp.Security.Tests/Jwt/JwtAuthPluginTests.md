# tests/ConduitSharp.Security.Tests/Jwt/JwtAuthPluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwtAuthPluginTests.ExecuteAsync_NoAuthHeader_ShortCircuits401

- (was line 21) Bearer extraction failures — no handler involvement

## JwtAuthPluginTests.ExecuteAsync_MalformedToken_ShortCircuits401

- (was line 45) Token validation failures

## JwtAuthPluginTests.ValidateConfig_MissingSigningKey_Throws

- (was line 103) Happy path

## JwtAuthPluginTests.ExecuteAsync_MultipleProviders_SucceedsIfAnyProviderValidates

- (was line 117) Multiple Providers

## JwtAuthPluginTests.ExecuteAsync_ValidTokenMissingRequiredRole_ShortCircuits403

- (was line 201) requiredClaims (RBAC) — 403, not 401, on a valid token lacking permission

## JwtAuthPluginTests.ExecuteAsync_InvalidSignatureWithRequiredClaims_StillReturns401NotChecked

- (was line 239) A bad signature must short-circuit 401 before requiredClaims is ever evaluated.

## JwtAuthPluginTests.ValidateConfig_MalformedRequiredClaims_Throws

- (was line 252) ValidateConfig — fails fast on a malformed requiredClaims block
- (was line 258) Exercises the fail-fast path a route reload would hit.

## JwtAuthPluginTests.ExecuteAsync_ReadsConfigFromContextPluginConfig

- (was line 279) Config comes from context.PluginConfig (routes.json round-trip)
- (was line 285) The issuer constraint only exists in the JSON placed on the context — proving ExecuteAsync parses context.PluginConfig rather than any ambient default.

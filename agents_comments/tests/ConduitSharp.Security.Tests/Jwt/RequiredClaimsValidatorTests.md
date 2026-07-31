# tests/ConduitSharp.Security.Tests/Jwt/RequiredClaimsValidatorTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RequiredClaimsValidatorTests.Validate_NullRules_ReturnsNull

- (was line 12) No rules configured

## RequiredClaimsValidatorTests.Validate_ExistenceOnly_ClaimPresent_ReturnsNull

- (was line 28) Existence-only (no matcher)

## RequiredClaimsValidatorTests.Validate_Equals_StringMatch_ReturnsNull

- (was line 47) equals

## RequiredClaimsValidatorTests.Validate_AnyOf_ArrayClaimIntersects_ReturnsNull

- (was line 73) anyOf — array claim (Entra app roles)

## RequiredClaimsValidatorTests.Validate_AllOf_DelimitedScopeContainsAll_ReturnsNull

- (was line 100) allOf + delimiter — space-delimited scopes (Entra/Okta scp)

## RequiredClaimsValidatorTests.Validate_AllOf_NoDelimiter_TreatsWholeStringAsOneElement

- (was line 128) Without a delimiter, "reports.read reports.write" is a single opaque value — allOf against two distinct scopes can never match.

## RequiredClaimsValidatorTests.Validate_NestedPath_ResolvesThroughObjectGraph

- (was line 139) Nested claim path (Keycloak realm_access.roles)

## RequiredClaimsValidatorTests.Validate_LiteralNameWithDots_PrefersLiteralOverPathTraversal

- (was line 159) Literal-name precedence (Auth0 namespaced claim containing dots)
- (was line 165) "https://example.com/roles" contains no dots that would form a nested path here, but this proves the literal top-level lookup is tried first regardless.

## RequiredClaimsValidatorTests.Validate_DottedLiteralName_NotShadowedByAccidentalNestedPath

- (was line 175) A literal key containing a dot ("a.b") must win over interpreting it as a path into a nested object also named "a" with property "b".

## RequiredClaimsValidatorTests.Validate_MultipleRules_AllPass_ReturnsNull

- (was line 183) Multiple rules — logical AND, first failure wins

# tests/ConduitSharp.Security.Tests/Jwt/JwtAuthHandlerTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwtAuthHandlerTests.Malformed_ReturnsFalse

- (was line 27) Structure / malformed input — must fail validation, never throw

## JwtAuthHandlerTests.ValidBase64ButInvalidJsonPayload_ReturnsFalse

- (was line 46) Valid Base64Url that decodes to non-JSON must fail validation, not throw (used to bubble up as a 500).

## JwtAuthHandlerTests.UnsupportedConfigAlgorithm_ReturnsFalse

- (was line 68) Algorithm / signature checks

## JwtAuthHandlerTests.AlgNoneToken_ReturnsFalse

- (was line 84) alg-confusion guard: an unsigned "none" token must never validate.

## JwtAuthHandlerTests.ExpiredToken_ReturnsFalse

- (was line 124) Lifetime

## JwtAuthHandlerTests.TokenWithoutExp_IsValid

- (was line 150) Pre-existing gateway behavior: exp is honored when present but not required.

## JwtAuthHandlerTests.WrongIssuer_ReturnsFalse

- (was line 157) Issuer / audience

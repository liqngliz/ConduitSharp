# tests/ConduitSharp.Security.Tests/Jwt/JwksJwtAuthHandlerTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwksJwtAuthHandlerTests.Config

- (was line 11) Helpers — real RSA/EC keys, canned key provider (no HTTP)

## JwksJwtAuthHandlerTests.Malformed_ReturnsFalse

- (was line 34) Structure checks

## JwksJwtAuthHandlerTests.UnsupportedAlgorithm_ReturnsFalse

- (was line 51) Algorithm checks — symmetric and unsigned algs are rejected up front

## JwksJwtAuthHandlerTests.KeyProviderReturnsNull_WithKid_ReturnsKidError

- (was line 67) Key lookup failures

## JwksJwtAuthHandlerTests.ValidRs256Token_ReturnsTrue_AndExposesClaims

- (was line 103) Signature verification — real crypto, both key types

## JwksJwtAuthHandlerTests.ExpiredToken_ReturnsFalse

- (was line 150) Claims — lifetime, issuer, audience (validated by the library)

## JwksJwtAuthHandlerTests.SignedGarbagePayload_ReturnsFalse_NotThrow

- (was line 214) Correctly signed token whose payload is not JSON — must be a validation failure, not an unhandled exception (used to bubble up as a 500).

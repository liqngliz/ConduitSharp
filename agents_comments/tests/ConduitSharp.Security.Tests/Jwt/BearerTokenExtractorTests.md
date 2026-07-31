# tests/ConduitSharp.Security.Tests/Jwt/BearerTokenExtractorTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## BearerTokenExtractorTests.Extract_NullHeader_ReturnsRequiredError

- (was line 9) Missing / empty header

## BearerTokenExtractorTests.Extract_BasicScheme_ReturnsBearerError

- (was line 37) Wrong auth scheme

## BearerTokenExtractorTests.Extract_ValidBearer_ReturnsToken

- (was line 65) Valid extraction

## BearerTokenExtractorTests.Extract_EmptyTokenAfterPrefix_ReturnsEmptyString

- (was line 103) Downstream handler is responsible for rejecting empty tokens

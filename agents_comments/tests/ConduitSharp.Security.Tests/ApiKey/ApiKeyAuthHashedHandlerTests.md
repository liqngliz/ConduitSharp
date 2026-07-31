# tests/ConduitSharp.Security.Tests/ApiKey/ApiKeyAuthHashedHandlerTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## ApiKeyAuthHashedHandlerTests.IsValid_CorrectKey_ReturnsTrue

- (was line 14) Match

## ApiKeyAuthHashedHandlerTests.IsValid_WrongKey_ReturnsFalse

- (was line 45) No match

## ApiKeyAuthHashedHandlerTests.IsValid_MalformedHexEntry_SkipsAndReturnsFalse

- (was line 66) Malformed hash entries are skipped rather than throwing

## ApiKeyAuthHashedHandlerTests.IsValid_TruncatedHash_ReturnsFalse

- (was line 87) Wrong-length hash never matches (32 bytes required for SHA-256)
- (was line 93) 16 bytes instead of 32

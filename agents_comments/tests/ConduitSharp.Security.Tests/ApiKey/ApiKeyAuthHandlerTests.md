# tests/ConduitSharp.Security.Tests/ApiKey/ApiKeyAuthHandlerTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## ApiKeyAuthHandlerTests.IsValid_KeyInList_ReturnsTrue

- (was line 9) Match

## ApiKeyAuthHandlerTests.IsValid_WrongKey_ReturnsFalse

- (was line 29) No match

## ApiKeyAuthHandlerTests.IsValid_WrongCase_ReturnsFalse

- (was line 57) Case sensitivity — keys are compared byte-for-byte

## ApiKeyAuthHandlerTests.IsValid_PrefixOfAllowedKey_ReturnsFalse

- (was line 69) Different lengths cannot match (FixedTimeEquals requires same length)

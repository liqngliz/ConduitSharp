# tests/ConduitSharp.Security.Tests/Jwt/JwtAuthConfigTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwtAuthConfigTests

- (was line 11) base64("demo-signing-key-conduitsharp-example-32ch") — 43 raw bytes, over the 32-byte HS256 minimum enforced at load time.

## JwtAuthConfigTests.From_FullConfig_BindsAllFields

- (was line 16) Deserialisation

## JwtAuthConfigTests.From_OnlySigningKey_UsesDefaults

- (was line 54) default optional, absent → null optional, absent → null

## JwtAuthConfigTests.From_MissingSigningKey_Throws

- (was line 60) signingKey validation — fails at load time, not on the first request

## JwtAuthConfigTests.From_RawPassphraseSigningKey_ThrowsWithBase64Hint

- (was line 73) The classic interop landmine: a raw secret ("your-256-bit-secret") pasted in instead of its base64 encoding used to reject every token at runtime with no hint.

## JwtAuthConfigTests.From_SigningKeyUnder32Bytes_Throws

- (was line 83) "dGVzdC1rZXk=" = base64("test-key") — valid base64 but only 8 bytes.

## JwtAuthConfigTests.From_NoRequiredClaims_IsNull

- (was line 90) requiredClaims (RBAC)

## JwtAuthConfigTests.From_NullJson_Throws

- (was line 137) Null / invalid input

# src/ConduitSharp.Security/Jwt/JwtAuthConfig.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwtAuthConfig.SigningKey

- (was line 41) Legacy top-level fields

## JwtAuthConfig.ValidateSigningKey

- (was line 89) Startup guard for the classic interop landmine: a raw passphrase pasted into signingKey either isn't valid base64 or decodes under the HS256 minimum, and without this check every token is rejected at runtime with zero hint why.

## JwtAuthConfig

- Declared apart from JwtAuthPlugin because both the plugin and JwtAuthHandler read it. With the records in the plugin's file, the handler's dependency on them pointed back at the plugin that depends on the handler.

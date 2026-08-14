# src/ConduitSharp.Security/Jwt/JwtAuthHandler.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwtAuthHandler.TryValidate

- (was line 23) Fail closed on a non-HS256 config or a signing key that isn't valid base64 — same outcome as a signature that doesn't verify.
- (was line 42) Completes synchronously for symmetric keys — no I/O involved.

# src/ConduitSharp.Security/Jwt/JwtValidation.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwtValidation.ValidateAsync

- (was line 37) The token is verified — its payload segment is safe to parse.

## JwtValidation.BaseParameters

- (was line 48) exp/nbf are honored when present, but a token without exp stays valid (pre-existing gateway behavior; some internal issuers omit exp).

## JwtValidation.MapError

- (was line 62) Covers SecurityTokenSignatureKeyNotFoundException too (derived type).

# src/ConduitSharp.Security/Jwt/JwksJwtAuthPlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwksJwtAuthPlugin.ExecuteAsync

- (was line 56) Don't overwrite a 403 with a 401
- (was line 72) Successfully authenticated and authorized

## JwksJwtAuthPlugin.ValidateConfig

- (was line 81) Loading the config validates required fields (jwksUri), so a route with a missing or malformed jwks-jwt-auth config fails at startup instead of on the first request.

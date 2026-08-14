# src/ConduitSharp.Security/Jwt/JwtAuthPlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwtAuthPlugin.ExecuteAsync

- (was line 64) Successfully authenticated and authorized

## JwtAuthPlugin.ValidateConfig

- (was line 73) Loading the config validates requiredClaims shape, so a route with a malformed requiredClaims block fails at startup instead of on the first request.

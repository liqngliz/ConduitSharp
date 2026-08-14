# src/ConduitSharp.Security/Jwt/JwksJwtAuthHandler.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwksJwtAuthHandler.TryValidateAsync

- (was line 29) Parse just enough to know which key to fetch (alg + kid); full validation happens below once the key is in hand.
- (was line 44) We set timeout on the HTTP client during factory construction or via the CancellationToken
- (was line 50) Force timeout enforcement even if ConfigurationManager swallows the CancellationToken internally
- (was line 59) If kid is provided, verify it exists in the downloaded keys

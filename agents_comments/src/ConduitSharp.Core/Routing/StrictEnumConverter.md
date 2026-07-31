# src/ConduitSharp.Core/Routing/StrictEnumConverter.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## StrictEnumConverter.Read

- (was line 26) 1. Direct case-insensitive match (handles PascalCase and UPPERCASE inputs).
- (was line 30) 2. Kebab-case → PascalCase conversion ("jwt-auth" → "JwtAuth").

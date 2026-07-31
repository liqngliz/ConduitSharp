# src/ConduitSharp.Gateway.AspNetCore/Middleware/RequestBodyBudget.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RequestBodyBudget.TryReserveRam

- (was line 56) no RAM budget — nothing may be held in memory

## RequestBodyBudget.TryReserveDisk

- (was line 74) no disk budget — spilling is not permitted

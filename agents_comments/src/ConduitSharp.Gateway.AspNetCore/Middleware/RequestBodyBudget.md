# src/ConduitSharp.Gateway.AspNetCore/Middleware/RequestBodyBudget.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RequestBodyBudget.TryReserveRam

- (was line 56) no RAM budget — nothing may be held in memory

## RequestBodyBudget.TryReserveDisk

- (was line 74) no disk budget — spilling is not permitted

## RequestBodyBudget

- Two budgets rather than one combined number, deliberately. A single 'RAM + disk' total cannot be sized correctly: raising it to suit a large spill volume would silently license the same growth in RAM, which is how the process gets OOM-killed instead of shedding.
- The RAM reservation covers the buffer's capacity, not its fill, because FileBufferingReadStream rents the whole threshold from ArrayPool at construction and returns it the instant it spills. A caller reserves the threshold up front; if the body outgrows it, the caller releases the RAM reservation and charges the bytes to the disk budget instead.
- Disk reservations are made chunk-by-chunk as the body is read and released when the request completes.

# src/ConduitSharp.Core/Pipeline/BoundedTeeStream.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## BoundedTeeStream

- Write-through rather than hold-back, so the client keeps receiving bytes as they arrive and a plugin can inspect a response without turning a streaming reply into a buffered one. Measured: a 5-chunk SSE response delivers 5 reads with the first at 38 ms through the tee, versus 1 read at 1008 ms under a MemoryStream swap.
- Swapping HttpResponse.Body also reroutes BodyWriter through it, so YARP's forward is captured too.
- Both buffer exits are idempotent because returning one array to the pool twice corrupts every later renter, which is far worse than leaking it once.
- ASP.NET Core has this class as ResponseBufferingStream and keeps it internal; its Initialize takes three HttpLogging-private types, so reflection cannot reach it either. That is why this exists rather than being borrowed.

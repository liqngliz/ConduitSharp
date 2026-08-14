# plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/JsonlSpendStore.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JsonlSpendStore..ctor

- (was line 46) DropWrite: losing a spend row under a disk stall is the right trade against making a caller wait on one. The queue holds ~1000 calls, far more than any real burst.

## JsonlSpendStore.Read

- (was line 78) A row torn by a crash mid-write. Skip it rather than lose the whole day.

## JsonlSpendStore.HashCaller

- (was line 92) 12 bytes is 96 bits of collision resistance, plenty to keep callers distinct, and short enough to read in a file. Truncation is what makes it non-reversible in practice.

## JsonlSpendStore.DrainAsync

- (was line 103) ponytail: opens the file per row. LLM calls arrive seconds apart, so a handle per row is free and buys crash-durability without any flush bookkeeping. Batch by draining the channel into one append if this ever sees writes-per-second.
- (was line 112) Disk full, permissions, a locked file: a spend row is not worth taking the gateway down for. The next row tries again.

## JsonlSpendStore.LoadOrCreateSalt

- (was line 126) The salt must outlive the process or the same caller hashes differently tomorrow and the history stops grouping. ponytail: two processes creating it at once means one keeps a salt the other overwrote; on a single-user local store that self-heals on restart.

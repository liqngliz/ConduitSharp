# tests/ConduitSharp.Integration.Tests/Gateway/RemovedRequestLimitKeysTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RemovedRequestLimitKeysTests.NegativeBudget_IsRejected_RatherThanSilentlyCoerced

- (was line 57) 0 already means "none of this resource", so a negative has no meaning to fall back on.

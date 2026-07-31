# tests/ConduitSharp.Integration.Tests/Gateway/PipelineTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PipelineTests.ThrowingPlugin_Returns500_NotUnobservedException

- (was line 52) A plugin that throws must surface as a clean 500 — not an unhandled exception that exits the pipeline without a response — and must not forward to the upstream.
- (was line 65) exception detail not leaked

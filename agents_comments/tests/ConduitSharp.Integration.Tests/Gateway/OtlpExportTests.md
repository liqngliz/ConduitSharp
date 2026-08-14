# tests/ConduitSharp.Integration.Tests/Gateway/OtlpExportTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## OtlpExportTests.GatewayRequest_WithOtlpEnabled_ExportsSpanToCollector

- (was line 40) Spans go through the batch export processor (the SDK default, ~5 s scheduled delay) and the HTTP POST to the collector is async, hence the polling window.

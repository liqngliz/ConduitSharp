# src/ConduitSharp.Gateway.AspNetCore/Telemetry/FileSpanExporter.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## FileSpanExporter.Export

- (was line 38) Instrumentation scope (OTel): which ActivitySource emitted the span and its version — the same identity a real OTLP exporter records per scope.

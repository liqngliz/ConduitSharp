# src/ConduitSharp.Observability/Telemetry/GatewayTelemetry.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GatewayTelemetry

- (was line 17) Scope version tracks the package version automatically (Directory.Build.props <Version> → AssemblyInformationalVersion, read back here). Same value on the ActivitySource and the Meter so this scope never reports a conflicting version across signals. Split('+') drops SourceLink's "+<gitcommit>" suffix.

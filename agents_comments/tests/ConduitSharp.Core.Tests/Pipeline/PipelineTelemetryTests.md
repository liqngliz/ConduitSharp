# tests/ConduitSharp.Core.Tests/Pipeline/PipelineTelemetryTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PipelineTelemetryTests.ActivitySource_Version_TracksAssemblyInformationalVersion

- (was line 16) The scope version is read from the package version, not hardcoded.
- (was line 19) SourceLink's "+<gitcommit>" suffix is stripped.
- (was line 21) Guards against reverting to the old hardcoded value.

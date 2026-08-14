# src/ConduitSharp.Core/Pipeline/PipelineTelemetry.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PipelineTelemetry

- (was line 16) The instrumentation-scope version tracks the package version (Directory.Build.props <Version>) automatically: MSBuild bakes it into AssemblyInformationalVersion, and we read it back here so telemetry never drifts from the release. Split('+') drops the "+<gitcommit>" SourceLink appends, leaving a clean SemVer like "1.0.0-rc.1".

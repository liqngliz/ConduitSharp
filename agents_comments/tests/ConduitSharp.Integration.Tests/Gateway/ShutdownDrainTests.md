# tests/ConduitSharp.Integration.Tests/Gateway/ShutdownDrainTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## ShutdownDrainTests.ConfiguredShutdownTimeout_IsAppliedToHost

- (was line 23) Set via env var, not the in-memory override: UseShutdownTimeout reads the options eagerly at host-build time (before WebApplicationFactory's in-memory config is layered in), and env vars are already present at that point — exactly as in production.
- (was line 31) force the host to build

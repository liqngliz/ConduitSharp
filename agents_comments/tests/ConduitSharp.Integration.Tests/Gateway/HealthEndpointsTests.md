# tests/ConduitSharp.Integration.Tests/Gateway/HealthEndpointsTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## HealthEndpointsTests.Healthz_Returns200_EvenWhenUpstreamIsDown

- (was line 21) Route forwards to a dead upstream — gateway liveness must not depend on it.

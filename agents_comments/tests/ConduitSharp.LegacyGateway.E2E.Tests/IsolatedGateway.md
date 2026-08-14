# tests/ConduitSharp.LegacyGateway.E2E.Tests/IsolatedGateway.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## IsolatedGateway.StartAsync

- (was line 113) Any HTTP response (404 included) means the pipeline is serving.

## IsolatedGateway.DisposeAsync

- (was line 140) teardown best-effort

## IsolatedGateway.BuildProjectAsync

- (was line 159) Exit-gated drain: MSBuild node-reuse workers inherit the pipes and outlive the build, so never wait for stream EOF (same lesson as LegacyGatewayFixture).

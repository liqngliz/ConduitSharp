# tests/ConduitSharp.Grafana.E2E.Tests/GrafanaStackFixture.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GrafanaStackFixture.IsDockerAvailableAsync

- (was line 61) Docker helpers

## GrafanaStackFixture.WaitForAsync

- (was line 122) not ready yet

## GrafanaStackFixture.PollUntilAsync

- (was line 131) Polling helper for the backend query APIs — telemetry lands asynchronously (gateway export → collector batch (5s) → backend ingest).
- (was line 157) transient — keep polling

## GrafanaStackFixture.LocateLegacyGatewayRoot

- (was line 165) Solution-root discovery

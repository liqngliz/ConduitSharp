# tests/ConduitSharp.Integration.Tests/Pipeline/Security/ApiKeyAuthEndToEndTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## ApiKeyAuthEndToEndTests.NoKeyHeader_Returns401

- (was line 18) api-key-auth (plaintext)

## ApiKeyAuthEndToEndTests.Same_plugin_on_four_routes_keeps_separate_configs

- (was line 63) Overlapping key lists across routes: a merge or overwrite of any route's config breaks at least one cell of the 4x4 matrix below.

## ApiKeyAuthEndToEndTests.Hashed_same_plugin_on_four_routes_keeps_separate_configs.H

- (was line 100) Same matrix as the plaintext variant, keys stored as SHA-256 hashes.

## ApiKeyAuthEndToEndTests.HashedRoutes

- (was line 134) api-key-auth-hashed (SHA-256)

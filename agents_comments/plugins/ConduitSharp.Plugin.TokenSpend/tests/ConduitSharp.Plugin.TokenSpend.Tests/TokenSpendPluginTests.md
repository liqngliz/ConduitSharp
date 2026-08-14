# plugins/ConduitSharp.Plugin.TokenSpend/tests/ConduitSharp.Plugin.TokenSpend.Tests/TokenSpendPluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## TokenSpendPluginTests.FakeStore

- (was line 10) Captures rows synchronously so a test can assert on them the moment ExecuteAsync returns, and echoes the caller key back unhashed so key routing stays visible. Hashing itself is covered against the real store in JsonlSpendStoreTests.

## TokenSpendPluginTests.EventStream_IsRecordedAsUncounted_RatherThanMissing

- (was line 93) Usage really is in this SSE body; the point is that this build does not parse it, and says so in the row instead of leaving the call out of the history entirely.

## TokenSpendPluginTests.RequestBody_YieldsModelTurnIndexAndStableSessionId

- (was line 144) Every turn resends the opening message, so the session groups without a client header.

## TokenSpendPluginTests.ToolTraffic_IsCountedInBothWireFormats

- (was line 170) OpenAI: a tool role plus a tool_calls array. Anthropic: typed content blocks.
- (was line 184) 2 calls + 1 tool role
- (was line 188) tool_use + tool_result

## TokenSpendPluginTests.CallerComesFromTheConfiguredHeader

- (was line 202) FakeStore echoes rather than hashes; the real store's hashing is asserted separately.

## TokenSpendPluginTests.Jwt

- (was line 206) Padding is stripped, the way a real token carries it, so the plugin has to restore it.

## TokenSpendPluginTests.CallerFallsBackToTheJwtClaim_WhenNoKeyHeaderMatches

- (was line 224) Neither header present, and a token that is not a JWT at all: one shared bucket, no throw.

## TokenSpendPluginTests.ValidateConfig_RejectsMissingUsageFields

- (was line 253) does not throw

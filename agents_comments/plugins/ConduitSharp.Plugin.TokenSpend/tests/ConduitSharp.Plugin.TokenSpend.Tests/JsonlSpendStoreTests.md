# plugins/ConduitSharp.Plugin.TokenSpend/tests/ConduitSharp.Plugin.TokenSpend.Tests/JsonlSpendStoreTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JsonlSpendStoreTests.RowsSurviveRestart_AcrossDayFiles_AndTheRangeIsRespected

- (was line 35) Dispose drains the queue
- (was line 37) A second instance proves the history is on disk, not in the first store's memory.

## JsonlSpendStoreTests.CallerHashIsStableAcrossRestarts_AndDistinctPerKey

- (was line 90) The salt is persisted, so yesterday's rows still group with today's.

## JsonlSpendStoreTests.ATornLineIsSkipped_RatherThanLosingTheDay

- (was line 135) Simulate a crash mid-write: a partial JSON object appended after a good row.

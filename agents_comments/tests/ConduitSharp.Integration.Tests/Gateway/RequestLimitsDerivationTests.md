# tests/ConduitSharp.Integration.Tests/Gateway/RequestLimitsDerivationTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RequestLimitsDerivationTests.RaisingTheDiskBudget_DoesNotRaiseTheRamBudget

- (was line 28) The safety property: 10 GiB of spill capacity must leave RAM at its own default.
- (was line 32) untouched

## RequestLimitsDerivationTests.RaisingTheRamBudget_DoesNotRaiseTheDiskBudget

- (was line 41) untouched

## RequestLimitsDerivationTests.EachBudget_AcceptsZero_MeaningNoneOfThatResource

- (was line 47) 0 reads the same way on both: none of that resource is available. RAM 0 => everything spills; disk 0 => a body must fit RAM or be shed.

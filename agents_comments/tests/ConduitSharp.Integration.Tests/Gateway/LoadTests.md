# tests/ConduitSharp.Integration.Tests/Gateway/LoadTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## LoadTests.ConcurrentRequests_100Parallel_AllReturn200

- (was line 35) Concurrency

## LoadTests.SequentialRequests_300_AllReturn200

- (was line 91) Sustained throughput

## LoadTests.MemoryStableUnderLoad_NoUnboundedGrowth

- (was line 125) Memory stability
- (was line 131) Warm up to stabilise the heap before taking a baseline.
- (was line 151) 50 MB

## LoadTests.ConcurrentRequestsThroughApiKeyAuth_AllAuthenticateCorrectly

- (was line 159) Concurrency through auth plugin

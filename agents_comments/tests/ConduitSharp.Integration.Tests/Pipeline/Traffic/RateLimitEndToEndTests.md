# tests/ConduitSharp.Integration.Tests/Pipeline/Traffic/RateLimitEndToEndTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RateLimitEndToEndTests.OverLimit_Returns429WithRetryAfter

- (was line 30) maxRequests=1: first call passes, second is blocked
- (was line 40) Retry-After reports the seconds remaining in the current fixed window, not the full window length.

## RateLimitEndToEndTests.Same_plugin_on_four_routes_keeps_separate_configs_and_counters

- (was line 51) Distinct quotas per route; counters are keyed by route id, so exhausting one route must neither consume nor widen another's quota.

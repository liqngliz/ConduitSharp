# tests/ConduitSharp.Traffic.Tests/RateLimitPluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RateLimitPluginTests.ConfiguredPlugin

- (was line 13) Helpers

## RateLimitPluginTests.ExecuteAsync_UnderLimit_CallsNext

- (was line 66) Under limit

## RateLimitPluginTests.ExecuteAsync_OverLimit_ShortCircuits429

- (was line 83) Over limit
- (was line 91) consume the one permit
- (was line 98) Assert.Contains("Rate limit exceeded", blocked.ShortCircuitBody);

## RateLimitPluginTests.ExecuteAsync_OverLimit_RetryAfterIsTimeRemainingInWindow

- (was line 110) Not the full window length — the seconds left until this fixed window rolls over.

## RateLimitPluginTests.ExecuteAsync_NoKeyHeader_AllRequestsShareGlobalCounter

- (was line 116) Global key (no KeyHeader configured)
- (was line 122) no keyHeader — all requests use "global"

## RateLimitPluginTests.ExecuteAsync_PerClientKey_DifferentClientsHaveIndependentLimits

- (was line 132) Per-client key via header
- (was line 146) different client — own quota

## RateLimitPluginTests.ExecuteAsync_KeyHeaderAbsent_FallsBackToGlobalKey

- (was line 156) No X-Client-Id header — falls back to "global"
- (was line 159) also no header — same "global" slot

## RateLimitPluginTests.ExecuteAsync_DifferentRoutes_HaveIndependentLimiters

- (was line 180) Different routes have independent limiters

## RateLimitPluginTests.ExecuteAsync_SharedStore_DifferentRoutesSameConfig_DoNotShareCounters

- (was line 206) The DI path hands every limiter the same IRateLimitStore singleton. The store key must therefore carry the route id — without it, two routes with identical window/max and the same caller consumed one shared quota.
- (was line 214) consumes route-a's single permit
- (was line 220) own quota, untouched by route-a

## RateLimitPluginTests.ValidateConfig_NonPositiveLimits_Throws

- (was line 225) ValidateConfig — rejects non-positive limits at startup

## RateLimitPluginTests.ValidateConfig_ValidLimits_DoesNotThrow

- (was line 246) no throw

# tests/ConduitSharp.Integration.Tests/Pipeline/Security/JwksJwtAuthEndToEndTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## JwksJwtAuthEndToEndTests.SlowJwks_ReturnsErrorWithinTimeout

- (was line 51) The JWKS endpoint stalls for 5 s; the route allows only 500 ms to fetch it. A well-formed RS256 token reaches the fetch step, so the timeout — not the full upstream delay — must decide the outcome: 401, returned promptly.

## JwksJwtAuthEndToEndTests.WellFormedRs256Token

- (was line 81) Structurally valid RS256 token (real header + payload, dummy signature). Enough to pass parsing and the algorithm check so the handler proceeds to the JWKS fetch.

## JwksJwtAuthEndToEndTests.SlowJwks_FirstRequestTimesOut_BackgroundFetchCompletes_SecondRequestHitsCache

- (was line 116) First request: hits the 500ms timeout circuit breaker, returns 401
- (was line 124) Wait enough time for the background fetch to finish the 1000ms delay and populate the cache
- (was line 127) Second request: background fetch is already done, cache is populated. It will instantly see the empty keys array and return 401 without any network delay!

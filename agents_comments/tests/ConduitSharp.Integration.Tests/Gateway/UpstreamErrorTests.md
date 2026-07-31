# tests/ConduitSharp.Integration.Tests/Gateway/UpstreamErrorTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## UpstreamErrorTests.ConnectionRefused_Returns502

- (was line 20) Port 1 on loopback is not listening — guaranteed connection refused.

## UpstreamErrorTests.RouteTimeoutMs_IsEnforced_Returns504

- (was line 49) The route's own timeoutMs (not an HttpClient override) must bound the upstream: a 30 s upstream against a 300 ms route timeout returns 504 well under 30 s.

## UpstreamErrorTests.RoutesWithRetry

- (was line 95) R2 — upstream retry on transient failure

## UpstreamErrorTests.FailThenSucceed

- (was line 114) Fails the first `failures` calls with 503, then serves 200.

## UpstreamErrorTests.TransientUpstreamFailure_IsRetried_ReturnsSuccess

- (was line 138) one failed, one retried

## UpstreamErrorTests.RetriesExhausted_ReturnsLastResponse

- (was line 157) always fails within the attempt budget
- (was line 164) 1 initial + 2 retries

## UpstreamErrorTests.NonIdempotentMethod_IsNotRetried_EvenWhenConfigured

- (was line 170) POST may already have been processed upstream — retrying could double-apply it.
- (was line 180) no retry for POST

## UpstreamErrorTests.RoutesWithRetryBlock

- (was line 184) upstream.retry block — the full policy surface (maxAttempts / backoff / retryOn)

## UpstreamErrorTests.RetryBlock_MaxAttemptsWithBackoff_RetriesUntilSuccess

- (was line 215) two failed, third served

## UpstreamErrorTests.RetryBlock_RetryOn_CustomStatusCode_IsRetried

- (was line 221) 500 is not retryable by default; retryOn opts it in.

## UpstreamErrorTests.RetryBlock_StatusOutsideRetryOn_IsNotRetried

- (was line 244) fails with 503
- (was line 252) 503 not in retryOn — passed through

## UpstreamErrorTests.IdempotentMethodWithBody_RetryResendsFullBody

- (was line 258) Regression: each attempt's HttpRequestMessage is now disposed, so the retry must rebuild its content from the buffered body rather than depend on the first attempt's (previously leaked) message keeping the stream open.

## UpstreamErrorTests.NonIdempotentMethod_WithRetryNonIdempotent_IsRetried_FullBodyResent

- (was line 276) Opt-in: retryNonIdempotent makes a POST replay. Proves all three call sites agree — the gate lets it through, the route buffers instead of streaming, and the loop rewinds the buffered body so attempt 2 carries the full payload (not an empty stream).
- (was line 288) POST retried once

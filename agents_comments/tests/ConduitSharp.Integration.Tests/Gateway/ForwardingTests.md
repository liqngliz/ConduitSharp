# tests/ConduitSharp.Integration.Tests/Gateway/ForwardingTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## ForwardingTests.Post_WithBody_NoContentType_ForwardsBodyWithoutContentTypeHeader

- (was line 67) A POST with a body but no Content-Type header exercises the no-Content-Type branch in GatewayMiddlewareExt.

## ForwardingTests.Post_WithEntityHeaders_ForwardsAllContentHeadersToUpstream

- (was line 83) Regression: only Content-Type used to be copied onto the outgoing content — Content-Encoding, Content-Language, etc. were silently dropped.

## ForwardingTests.Upstream_HopByHopResponseHeaders_AreNotRelayedToClient

- (was line 102) Hop-by-hop headers describe the gateway↔upstream connection; the request side already stripped them and the response side must do the same.

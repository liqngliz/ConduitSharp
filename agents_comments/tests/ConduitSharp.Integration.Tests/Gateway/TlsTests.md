# tests/ConduitSharp.Integration.Tests/Gateway/TlsTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## TlsTests.Request_SkipCertificateVerificationTrue_RoutesSuccessfully

- (was line 10) skipCertificateVerification selects the "upstream-insecure" HttpClient. The fake upstream is plain HTTP so the request succeeds regardless — this test covers the branch in GatewayMiddleware that selects the client.

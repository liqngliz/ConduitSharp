# tests/ConduitSharp.Mtls.E2E.Tests/MtlsE2ETests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## MtlsE2ETests.Gateway_with_client_cert_completes_mTLS_and_control_gateway_is_rejected

- (was line 31) Build the gateway image and start upstream + both gateways.
- (was line 39) With the client cert → the upstream verifies it and returns 200.
- (was line 45) Without the client cert → the upstream rejects the handshake (nginx 400).

## MtlsE2ETests.WaitForHealthAsync

- (was line 68) not up yet

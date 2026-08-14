# tests/ConduitSharp.Mtls.E2E.Tests/CertificateMaterial.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## CertificateMaterial

- (was line 14) must match the compose service name

## CertificateMaterial.Generate

- (was line 23) --- Certificate authority (self-signed, can sign other certs) ---
- (was line 35) --- Upstream server cert (SAN=upstream), signed by the CA ---
- (was line 42) serverAuth
- (was line 51) --- Gateway client cert, signed by the CA, exported as a password-protected PKCS#12 ---
- (was line 57) clientAuth

## CertificateMaterial.NextSerial

- (was line 64) A positive, random 16-byte serial number.

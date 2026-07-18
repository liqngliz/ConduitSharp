# mTLS end-to-end test

Verifies the gateway's per-route **client-certificate (mutual TLS)** support against a real
upstream that requires one — a genuine TLS handshake, which the in-process
[`EmbeddedGatewayTests`](../ConduitSharp.Integration.Tests/Integration/EmbeddedGatewayTests.cs)
can only get as far as *loading* the cert, not presenting it.

## What it does

```
CertificateMaterial (.NET)   CA + server cert (SAN=upstream) + client.pfx
        │
        ▼
   upstream (nginx)          ssl_verify_client on  — rejects anyone without a valid client cert
        ▲            ▲
        │ mTLS ✓     │ no cert ✗
   gateway        gateway-nocert
   :8080          :8081
```

- **gateway** routes `https://upstream:443` with the client cert configured → upstream returns
  `200 … verify=SUCCESS`. Because `ssl_verify_client on`, a 200 is only possible if the gateway
  actually presented a valid client certificate during the handshake.
- **gateway-nocert** is identical but has no client cert → upstream rejects with `400 No
  required SSL certificate was sent`.

## Run

**Cross-platform (Windows / macOS / Linux)** — only needs Docker:

```bash
make test-e2e-mtls
# or: dotnet test tests/ConduitSharp.Mtls.E2E.Tests
```

The [`ConduitSharp.Mtls.E2E.Tests`](../ConduitSharp.Mtls.E2E.Tests) project generates the certs
with .NET's X509 APIs (no `openssl`) and drives `docker compose` through the CLI (no `bash`/`make`),
so it runs the same on every OS and skips gracefully when Docker is absent. It uses the
[`docker-compose.mtls.yml`](docker-compose.mtls.yml), [`nginx.conf`](nginx.conf),
[`routes.json`](routes.json), and the product [`Dockerfile`](../../Dockerfile) in this folder.

## Notes

- The gateway must **genuinely trust** the upstream's server cert: `SSL_CERT_FILE=/certs/ca.crt`
  adds our CA to the OpenSSL trust store .NET uses on Linux. `skipCertificateVerification` is
  *not* an option here — it selects the `upstream-insecure` HttpClient, which does not attach a
  client certificate (see `HttpProxyPlugin` client selection). Configuring both a client cert and
  `skipCertificateVerification` on one route is **rejected at startup** (they're mutually exclusive).
- Certs are throwaway (2-day validity) and are regenerated each run; `certs/` is git-ignored.
- Builds the gateway from the product [`Dockerfile`](../../Dockerfile), so this also exercises the
  shipped image on the host's architecture (amd64 or arm64).

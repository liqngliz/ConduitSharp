# TLS / HTTPS

_Part of the [ConduitSharp documentation](../README.md)._


### Inbound (clients calling your gateway)

Kestrel terminates inbound TLS. Add a `Kestrel` section to `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http":  { "Url": "http://0.0.0.0:80" },
      "Https": {
        "Url": "https://0.0.0.0:443",
        "Certificate": {
          "Path": "certs/mygateway.pfx",
          "Password": ""
        }
      }
    }
  }
}
```

Keep the password out of source control. Double-underscore maps to a nested key:

```bash
KESTREL__ENDPOINTS__HTTPS__CERTIFICATE__PASSWORD=your-password dotnet run
```

```
caller → HTTPS → mygateway.com:443 (Kestrel unwraps TLS) → ConduitSharp → http://upstream:8080
```

`routes.json` is identical for HTTP and HTTPS callers.

### Outbound (gateway calling upstream services)

**Upstream has a trusted certificate (Let's Encrypt, public CA)**

Nothing to configure. Use `https://` in the node URL; validation is automatic.

```json
"cluster": {
  "destinations": { "node-0": { "address": "https://order-service.internal:443" } }
}
```

**Upstream has a self-signed or internal CA certificate**

Set `dangerousAcceptAnyServerCertificate` on the cluster's HTTP client:

```json
"cluster": {
  "destinations": { "node-0": { "address": "https://order-service.internal:443" } },
  "httpClient": { "dangerousAcceptAnyServerCertificate": true }
}
```

> Use this only for internal services or development environments. Never enable it for public upstreams.

**Both legs secured (end-to-end TLS)**

Each leg carries its own certificate and validation rules:

```
caller → HTTPS → mygateway.com (Kestrel) → HTTPS → upstream-service:443
```

Kestrel cert (above) for inbound, `https://` node URL for outbound.

**Mutual TLS to upstream (mTLS)**

Per-route client certificates in `appsettings.json`. No code changes.

PFX file:

```json
{
  "Gateway": {
    "Tls": {
      "ClientCertificates": [
        {
          "routeId": "order-service-route",
          "path": "certs/client.pfx",
          "password": ""
        }
      ]
    }
  }
}
```

Windows machine certificate store (no PFX file to manage):

```json
{
  "Gateway": {
    "Tls": {
      "ClientCertificates": [
        {
          "routeId": "order-service-route",
          "storeThumbprint": "A1B2C3D4...",
          "storeName": "My",
          "storeLocation": "LocalMachine"
        }
      ]
    }
  }
}
```

Keep PFX passwords out of the file. Environment variable override:

```bash
Gateway__Tls__ClientCertificates__0__Password=secret
```

> **mTLS and `dangerousAcceptAnyServerCertificate` are mutually exclusive on a route.** Presenting a
> client certificate to a server you refuse to authenticate is not mutual authentication, so the
> gateway rejects the combination at startup instead of letting it look secure. For an mTLS upstream
> on an internal CA, trust the CA instead (e.g. `SSL_CERT_FILE=/certs/ca.crt` on Linux). Runnable
> Docker example of the full handshake:
> [tests/ConduitSharp.Mtls.E2E.Tests/assets](../tests/ConduitSharp.Mtls.E2E.Tests/assets) (`make test-e2e-mtls`).

---


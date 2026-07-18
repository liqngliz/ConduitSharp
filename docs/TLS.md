# TLS / HTTPS

_Part of the [ConduitSharp documentation](../README.md)._


### Inbound — clients calling your gateway

TLS termination on the inbound side is handled by Kestrel, not the gateway itself. Add a `Kestrel` section to `appsettings.json` pointing at your certificate:

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

Keep the password out of source control — use an environment variable instead. ASP.NET Core maps double-underscore to nested keys automatically:

```bash
KESTREL__ENDPOINTS__HTTPS__CERTIFICATE__PASSWORD=your-password dotnet run
```

The flow is:
```
caller → HTTPS → mygateway.com:443 (Kestrel unwraps TLS) → ConduitSharp → http://upstream:8080
```

Your `routes.json` stays exactly the same regardless of whether callers use HTTP or HTTPS.

### Outbound — gateway calling upstream services

**Upstream has a trusted certificate (Let's Encrypt, public CA)**

Nothing to configure. Use `https://` in your node URL and certificate validation happens automatically:

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

When callers use HTTPS and your upstreams also require HTTPS, both legs are independent:

```
caller → HTTPS → mygateway.com (Kestrel) → HTTPS → upstream-service:443
```

Configure Kestrel for the inbound cert (above) and set the upstream node URL to `https://` for the outbound leg. Each leg has its own certificate and validation rules.

**Mutual TLS to upstream (mTLS)**

Configure client certificates per route in `appsettings.json` — no code changes required.

Using a PFX file:

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

On Windows, use the machine certificate store instead (no PFX file to manage):

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

Keep PFX passwords out of the file — use an environment variable override:

```bash
Gateway__Tls__ClientCertificates__0__Password=secret
```

> **mTLS and `dangerousAcceptAnyServerCertificate` are mutually exclusive on a route.** Presenting a
> client certificate to a server you refuse to authenticate defeats the point of mTLS — it is
> *mutual*. The gateway rejects that combination at startup rather than letting it look secure. If the mTLS
> upstream uses an internal CA, trust the CA instead (e.g. `SSL_CERT_FILE=/certs/ca.crt` on
> Linux). A runnable Docker example of the full handshake lives in
> [tests/ConduitSharp.Mtls.E2E.Tests/assets](../tests/ConduitSharp.Mtls.E2E.Tests/assets) (`make test-e2e-mtls`).

---


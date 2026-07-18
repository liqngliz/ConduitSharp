# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | ✅ Yes    |

## Reporting a vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Email **oniplus.ar@gmail.com** with:

- A description of the vulnerability and its potential impact
- Steps to reproduce or a proof-of-concept
- The version(s) affected

You will receive an acknowledgement within **48 hours** and a status update within **7 days**.

If the vulnerability is confirmed, a patch will be released as soon as possible and you will be credited in the release notes unless you prefer to remain anonymous.

## Scope

In scope:
- Authentication bypass in `jwt-auth`, `jwks-jwt-auth`, `api-key-auth`, `api-key-auth-hashed`
- Route matching vulnerabilities that allow unauthorised access to protected routes
- Information disclosure via error messages, headers, or telemetry
- Admin API (`POST /admin/routes/reload`) authentication bypass
- Dependency vulnerabilities in ConduitSharp packages

Out of scope:
- Vulnerabilities in upstream services behind the gateway
- Issues requiring physical access to the server
- Social engineering

## Security design notes

- API key comparisons use `CryptographicOperations.FixedTimeEquals` to prevent timing attacks
- The admin API stores only the SHA-256 hash of the secret key — the raw key is never written to disk or config
- OTLP export calls are filtered from HttpClient instrumentation to prevent credential leakage in traces
- Demo credentials in `examples/LegacyGateway/` are intentionally weak — never use them in production

# ConduitSharp.Security

Authentication and authorization primitives for ConduitSharp plugins.

Provides interfaces and utilities for building security plugins:

- Token validation
- JWT/OIDC support
- Certificate-based mTLS
- Custom authorization logic

Implement the plugin interface to wire your auth scheme into per-route middleware.

Examples:
- OAuth2 / OIDC token validation
- mTLS client certificate inspection
- Custom header-based auth

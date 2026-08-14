# tests/ConduitSharp.EmbeddedGatewayPrefixed.E2E.Tests/EmbeddedGatewayPrefixedE2ETests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## EmbeddedGatewayPrefixedE2ETests.PostUpload_WithStreamOnly_Succeeds_AndNoBodyCapture

- (was line 23) Uploads — streamOnly, no body capture
- (was line 38) Verify that BodyCapture did NOT log this route (it's not configured on the upload route)

## EmbeddedGatewayPrefixedE2ETests.Gateway_ExecutesStandardAspNetCoreMiddleware

- (was line 48) Prefix-only behavior: the host owns everything outside "/api"
- (was line 54) Prove that standard ASP.NET Core middleware injected before the gateway wraps the YARP pipeline. The proxy passes through, and the middleware appends the response header.
- (was line 62) Assert the header set by `app.Use(...)` in Program.cs

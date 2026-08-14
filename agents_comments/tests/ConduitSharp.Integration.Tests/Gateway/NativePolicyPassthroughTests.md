# tests/ConduitSharp.Integration.Tests/Gateway/NativePolicyPassthroughTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## NativePolicyPassthroughTests.HeaderAuthHandler

- (was line 22) Minimal scheme: authenticates only when X-Test-User is present, with that name as a claim.

## NativePolicyPassthroughTests.RouteWithAuthorizationPolicy_ChallengesAnonymous_AndAdmitsAuthenticated

- (was line 84) rejected before the forward

# tests/ConduitSharp.Integration.Tests/Gateway/RoutingTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## RoutingTests.HeaderConstraint_FiltersByHeaderPresence

- (was line 46) match.headers is enforced by the native RouteConstraintMatcherPolicy — a request missing the required header must not match the route (404, no forward), while the same request carrying it forwards to the upstream.

## RoutingTests.WrongMethodOnMatchingPath_Returns405

- (was line 110) Right path, wrong verb is a 405 — endpoint routing's own answer. Regression: a catch-all fallback endpoint would match every path and turn this into a 404.

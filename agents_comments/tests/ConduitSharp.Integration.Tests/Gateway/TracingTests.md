# tests/ConduitSharp.Integration.Tests/Gateway/TracingTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## TracingTests.Request_WithActivityListener_SpanIsCreatedAndTagged

- (was line 13) Register an ActivityListener so GatewayTelemetry.ActivitySource.StartActivity() returns a non-null Activity. This covers the non-null branches of every activity?.SetTag(...) call in GatewayMiddleware.InvokeAsync.

## TracingTests.Request_NoMatchingRoute_SpanHasNoRouteId

- (was line 96) Route only matches /specific — /other will return 404

## TracingTests.Forwarding_ProducesForwardSpan

- (was line 127) The forward to the upstream is traced whether or not "http-proxy" is named in the plugin list — implicit and explicit forwarding produce the same trace shape.
- (was line 142) default route, no plugins

# tests/ConduitSharp.Integration.Tests/Pipeline/Transformation/HeaderTransformEndToEndTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## HeaderTransformEndToEndTests.Same_plugin_on_four_routes_keeps_separate_request_configs

- (was line 17) Four distinct request transforms; the upstream must see exactly the transform of the route that was hit, with no set/add/remove bleeding across routes.

## HeaderTransformEndToEndTests.Response_block_strips_and_adds_headers_before_the_client_sees_them

- (was line 71) The §3 capability, and the exact case the shipped routes.json config intends: an upstream response header the gateway removes on the way out, plus a security header it adds. Proves the response block reaches the client, not just the request block.

## HeaderTransformEndToEndTests.Flat_config_shape_fails_at_startup

- (was line 104) Regression guard for the silent no-op: the pre-2.0 flat shape ({ set, add, remove } at the top level) parsed to an empty transform and did nothing. It must now fail the load.

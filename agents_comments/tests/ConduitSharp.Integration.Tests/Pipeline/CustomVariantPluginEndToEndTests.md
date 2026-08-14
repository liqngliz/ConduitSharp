# tests/ConduitSharp.Integration.Tests/Pipeline/CustomVariantPluginEndToEndTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## CustomVariantPluginEndToEndTests.Same_variant_on_four_routes_keeps_separate_configs

- (was line 46) One singleton plugin instance serves all four routes — same widening matrix as the built-in api-key-auth test.

## CustomVariantPluginEndToEndTests.Two_variants_resolve_independently_per_route

- (was line 83) Both plugins share PluginName.Custom — routes must bind by variant, and each variant must see only its own route's config.

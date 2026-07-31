# src/ConduitSharp.Transformation/Plugins/HeaderTransformPlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## HeaderTransformPlugin.ValidateConfig

- (was line 37) Catch the pre-2.0 flat shape ({ "add", "set", "remove" } at the top level). It parses into an empty request/response and would silently do nothing on every request — which is exactly how a live config went dead unnoticed. Fail the load instead.
- (was line 51) Parse now so a malformed block fails at startup rather than on the first request.

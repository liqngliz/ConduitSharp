# tests/ConduitSharp.Integration.Tests/Gateway/PluginResolutionPrecedenceTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PluginResolutionPrecedenceTests.HostDiRegistration_ReplacesBuiltInOfSameName

- (was line 41) Route names the built-in api-key-auth with a valid config; the DI-registered plugin with the same PluginName must serve instead. The built-in would 401 this key-less request — the stamp proves the override took the route.
- (was line 51) no X-Api-Key header on purpose

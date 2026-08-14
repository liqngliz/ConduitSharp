# tests/ConduitSharp.Core.Tests/Routing/PluginNameExtensionsTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PluginNameExtensionsTests.ToId_MatchesWhatStrictEnumConverterAcceptsFromRoutesJson

- (was line 26) routes.json declares plugins with the same kebab-case spelling ToId() produces — this keeps the two in lockstep so a plugin's Id always matches the "name" a route config would use to select it.

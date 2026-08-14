# tests/ConduitSharp.Security.Tests/ApiKey/ApiKeyAuthPluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## ApiKeyAuthPluginTests.ExecuteAsync_NoHeader_ShortCircuits401

- (was line 27) Missing / empty header

## ApiKeyAuthPluginTests.ExecuteAsync_InvalidKey_ShortCircuits401

- (was line 53) Invalid key

## ApiKeyAuthPluginTests.ExecuteAsync_ValidKey_CallsNext

- (was line 68) Valid key

## ApiKeyAuthHashedPluginTests.ExecuteAsync_NoHeader_ShortCircuits401

- (was line 131) Missing header

## ApiKeyAuthHashedPluginTests.ExecuteAsync_WrongKey_ShortCircuits401

- (was line 146) Invalid key

## ApiKeyAuthHashedPluginTests.ExecuteAsync_ValidKey_CallsNext

- (was line 161) Valid key (supplied raw; hash is computed and compared)

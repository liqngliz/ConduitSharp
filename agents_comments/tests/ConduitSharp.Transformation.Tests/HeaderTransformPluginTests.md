# tests/ConduitSharp.Transformation.Tests/HeaderTransformPluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## HeaderTransformPluginTests.MakeContext

- (was line 17) Default response feature stores OnStarting callbacks but never fires them; swap in one that does, so response-block assertions exercise the real deferred path.

## HeaderTransformPluginTests.StartResponse

- (was line 30) Fires the registered Response.OnStarting callbacks the way the server would just before flush.

## HeaderTransformPluginTests.FiringResponseFeature

- (was line 34) Minimal IHttpResponseFeature that actually invokes OnStarting callbacks (in reverse registration order, as the server does) when the response starts.

## HeaderTransformPluginTests.Name_IsHeaderTransform

- (was line 56) Request block

## HeaderTransformPluginTests.Response_Remove_StripsUpstreamHeaderBeforeSend

- (was line 104) Response block — the new capability; applied via OnStarting

## HeaderTransformPluginTests.Response_NotAppliedUntilResponseStarts

- (was line 133) The mutation is deferred to OnStarting, so it must not touch the response before then.
- (was line 137) still there — response hasn't started

## HeaderTransformPluginTests.RequestAndResponse_BothApplied

- (was line 141) Both directions in one config

## HeaderTransformPluginTests.EmptyConfig_PassesThrough_AndCallsNext

- (was line 161) Empty / missing blocks — no-op, next still called

## HeaderTransformPluginTests.ValidateConfig_FlatShape_ThrowsWithMigrationHint

- (was line 178) ValidateConfig — the flat shape that silently no-op'd is now a startup error

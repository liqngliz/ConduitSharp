# tests/ConduitSharp.Integration.Tests/Routing/GatewayRouteTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GatewayRouteTests

- (was line 18) Inline copy of routes.json so the test has no dependency on filesystem paths.

## GatewayRouteTests.Deserialize_LoadsEveryRoute

- (was line 75) Deserialization — YARP's records bind from the same camelCase as ours

## GatewayRouteTests.HeaderMatch_BindsYarpsMatcherObjects_IncludingTheStringEnumMode

- (was line 109) The whole reason for taking YARP's types: header matching gains modes (Prefix, Contains, NotExists, …) the old dictionary shape could not express.

## GatewayRouteTests.InvalidPluginName_ThrowsJsonException

- (was line 138) Regression: registering JsonStringEnumConverter in the shared options would shadow PluginName's StrictEnumConverter (options converters beat type attributes), quietly breaking kebab-case and this error.

## GatewayRouteTests.RetryBlock_Deserializes_AllFields

- (was line 147) Reliability blocks — ours, not YARP's

## GatewayRouteTests.NoRetryOrCircuitBreaker_IsNull_NotADefaultedObject

- (was line 189) Absent means off — the gateway must not invent a retry policy nobody asked for.

## GatewayRouteTests.Validate_PassesForAWellFormedDocument

- (was line 197) Validate — route ids

## GatewayRouteTests.Validate_RejectsIdsThatCouldEscapeThePluginsRoot

- (was line 231) route ids become directory names under the plugins root

## GatewayRouteTests.Validate_ThrowsWhenCustomPluginHasNoVariant

- (was line 246) Validate — plugins

## GatewayRouteTests.Validate_PassesWhenCustomPluginHasVariant

- (was line 274) script-route

## GatewayRouteTests.ClusterWith

- (was line 277) Validate — cluster destinations (the gateway cares about scheme; YARP does not)

## GatewayRouteTests.Validate_ThrowsWhenDestinationIsNotHttp

- (was line 296) ftp:// is a valid absolute Uri — so it deserializes — but not a valid gateway upstream.

## GatewayRouteTests.Validate_ThrowsWhenDestinationIsARelativePath

- (was line 305) Careful: .NET parses a leading-slash path as an absolute file:// URI on Unix, so this gets caught by the scheme check rather than the absolute-URI check. Either way it must not reach the forwarder.

## GatewayRouteTests.RouteWith

- (was line 322) Validate — retry / circuit breaker

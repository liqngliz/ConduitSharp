# src/ConduitSharp.Gateway.AspNetCore/Routing/GatewayRoute.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GatewayRoutesConfiguration

- (was line 9) Root
- (was line 43) Order matters, and subtly: a converter in this collection beats a [JsonConverter] attribute on the type. Registering JsonStringEnumConverter alone would therefore shadow PluginName's StrictEnumConverter and break kebab-case ("jwt-auth"), so the strict converters go first and the general one only catches what is left — which is what YARP's enums (HeaderMatchMode, QueryParameterMatchMode, …) need.

## GatewayRoutesConfiguration.ValidatePluginVariants

- (was line 103) A variant disambiguates Custom plugins; it is meaningless (and a likely mistake) on a built-in plugin name, and required on Custom so a route resolves unambiguously.

## GatewayRoutesConfiguration.ValidateCluster

- (was line 119) A route that forwards must name at least one destination targeting http(s). YARP checks the address parses; it does not care about the scheme, and a gateway does.

## GatewayRoute

- (was line 171) Route

## RetryConfig

- (was line 260) Reliability — the things YARP has no concept of

## SwaggerOptions

- (was line 377) Swagger aggregation

## PluginConfig

- (was line 402) Plugins

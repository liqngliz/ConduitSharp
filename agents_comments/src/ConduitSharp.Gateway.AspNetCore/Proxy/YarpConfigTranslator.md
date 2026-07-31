# src/ConduitSharp.Gateway.AspNetCore/Proxy/YarpConfigTranslator.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## YarpConfigTranslator.Translate

- (was line 47) Routes and clusters are 1:1, so both ids are the route's id — never re-typed.
- (was line 51) Declaration order breaks overlaps: endpoint selection prefers the lowest Order, so the first route declared in routes.json wins. An explicit Order still wins.

## YarpConfigTranslator.WithCircuitBreaker

- (was line 66) The circuit breaker is a passive health-check policy. Only its *enablement* crosses into YARP — the threshold and cooldown are read from the route's own CircuitBreakerConfig, so nothing rides along in ClusterConfig.Metadata as a string. Any active health check the user configured is preserved.

## YarpConfigTranslator

- That shape is the point. A field-by-field translator is a layer that can disagree with YARP, and once did: it silently downgraded HTTP/2 and broke gRPC. It also has to grow every time YARP grows a feature. Neither is true of a `with` expression.

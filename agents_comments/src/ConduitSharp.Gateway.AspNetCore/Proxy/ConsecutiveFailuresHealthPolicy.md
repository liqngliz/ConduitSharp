# src/ConduitSharp.Gateway.AspNetCore/Proxy/ConsecutiveFailuresHealthPolicy.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## ConsecutiveFailuresHealthPolicy

- (was line 38) ponytail: never trimmed — bounded by (clusters x destinations), i.e. the size of routes.json.

## ConsecutiveFailuresHealthPolicy.RequestProxied

- (was line 44) Client went away mid-flight — tells us nothing about the node's health.
- (was line 47) ClusterId == RouteId, so the gateway half of this route's config is one lookup away.
- (was line 64) Reactivation resets the destination to Unknown, not Healthy, and leaves the counter at the threshold: a node that fails its trial request opens again immediately, while one that succeeds resets above.

## ConsecutiveFailuresHealthPolicy

- YARP's stock passive policy (TransportFailureRate) is rate-over-a-window rather than consecutive-count, so it cannot express the documented threshold. This one can, in about 40 lines, and replaced the whole NodeHealthTracker / ILoadBalancer tree.
- Thresholds come from the route's own CircuitBreakerConfig resolved by route id, since clusters are 1:1 with routes. Nothing rides on ClusterConfig.Metadata: gateway config stays typed on the gateway's side of the split, and a hot reload updates it with the rest.

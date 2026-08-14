# tests/ConduitSharp.Integration.Tests/Gateway/LoadBalancingTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## LoadBalancingTests.RoundRobin_MultipleNodes_DistributesAcrossAllNodes

- (was line 34) Two independent upstream servers — round-robin must alternate between them.
- (was line 55) Four requests — round-robin must visit each node exactly twice.

## LoadBalancingTests.YarpBuiltInPolicies_AreUsableByName

- (was line 82) loadBalancingStrategy is a YARP policy name, not a closed enum — every policy YARP registers is available without a schema change.

## LoadBalancingTests.UnknownLoadBalancingStrategy_FailsTheGatewayAtStartup_ListingWhatIsAvailable

- (was line 97) A typo (or a policy DLL that was never dropped in) must not sit dormant until the first request picks a node. The gateway validates the name against the registered ILoadBalancingPolicy set at startup, and the error names the route and what it could have used — rather than leaving it to YARP's later, terser complaint.
- (was line 112) the offending route ...and a valid one

## LoadBalancingTests.LoadBalancingPolicy_EnumRoundTripsToThePolicyName

- (was line 119) The enum exists so C# callers get compile-time safety; YARP declares its policy names as nameof(...), so ToString() is the wire value. If YARP ever renames one, this fails.

## LoadBalancingTests.DeadNode_IsCircuitBreakerOpenAfterFailures

- (was line 135) node1 always 503 (reachable but unhealthy — so we can count its hits); node2 always 200. Circuit opens after 2 failures; retry masks the transition. After it opens, node1 must stop being selected: it receives exactly `threshold` requests no matter how many the client sends, while node2 serves the rest.
- (was line 170) retry + failover keep it succeeding
- (was line 173) The unhealthy node is dropped after its circuit opens (2 failures), not hit once per round for all 10 requests. Passive health state propagates just after the failing response completes, so on a slow runner one extra request can pick the dead node before the open circuit lands — allow that single race, no more.

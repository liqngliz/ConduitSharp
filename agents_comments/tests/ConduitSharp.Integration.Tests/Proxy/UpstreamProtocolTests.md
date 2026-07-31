# tests/ConduitSharp.Integration.Tests/Proxy/UpstreamProtocolTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## UpstreamProtocolTests.InboundHttp2_SwapsInAnH2cPriorKnowledgeCluster

- (was line 55) Everything that is not the protocol is the cluster's own: the per-attempt timeout survives, and the HttpMessageInvoker (connection pool) is shared, not rebuilt.

## UpstreamProtocolTests.DerivedClusterIsCachedPerModel_NotRebuiltPerRequest

- (was line 83) Same source model → same derived model. A config reload produces a NEW ClusterModel, which naturally gets a fresh derivative — that is the eviction strategy.

## UpstreamProtocolTests.AlwaysCallsNext

- (was line 95) no proxy feature at all (plugin-only route)

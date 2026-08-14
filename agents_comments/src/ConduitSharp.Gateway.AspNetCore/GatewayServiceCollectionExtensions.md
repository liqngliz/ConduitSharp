# src/ConduitSharp.Gateway.AspNetCore/GatewayServiceCollectionExtensions.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GatewayServiceCollectionExtensions.AddConduitSharpGateway

- (was line 57) Stash the resolved composition options so UseConduitSharpGateway can read the same toggles back after Build() without the caller having to pass them twice.
- (was line 68) Drain in-flight requests on shutdown (including admin route reload) rather than cutting them off mid-response.
- (was line 99) Used by JwksJwtAuthPlugin to fetch the public key set from the identity provider. Upstream forwarding no longer uses IHttpClientFactory — YARP builds one HttpMessageInvoker per cluster (see UpstreamForwarderHttpClientFactory).
- (was line 121) Checked here rather than at AddConduitSharpGateway time so the whole configuration — including sources a host layers on after the gateway is wired — is visible.
- (was line 128) The two budgets meter different physical resources and are independent — neither clamps the other. Negatives are meaningless for both (0 already means "none of this resource"), so they are rejected rather than silently coerced.

## GatewayServiceCollectionExtensions.AddConduitSharpGateway.RejectRemovedRequestLimitKeys

- (was line 78) v2.0.0 replaced one RAM+disk total with two independent per-resource budgets. Silently ignoring the old keys would leave an upgraded deployment running on defaults while its config still looks deliberate — and the failure mode is an OOM, so this fails loudly.

## GatewayServiceCollectionExtensions.AddReverseProxy

- (was line 151) The proxy engine. routes.json is translated into YARP's route/cluster model and served from memory — YARP's own appsettings schema is never bound, so routes.json stays the product. YARP validates each cluster's loadBalancingStrategy against the registered ILoadBalancingPolicy set at load time, which is where an unknown strategy name is caught.
- (was line 164) Registered after AddReverseProxy so these win over YARP's TryAdd-ed defaults.

## GatewayServiceCollectionExtensions.AddPipelineAndBuiltInPlugins

- (was line 190) No http-proxy plugin: forwarding is YARP's ForwarderMiddleware, and the "http-proxy" entry in a route's plugin list just names where in the chain that forward happens. match.headers / match.queryParams are enforced natively by YARP's header and query MatcherPolicies, built from RouteMatch.Headers / RouteMatch.QueryParameters.

## GatewayServiceCollectionExtensions.AddObservability

- (was line 201) OpenTelemetry — traces and metrics. Console exporter: Gateway:Observability:Console:Enabled=true (dev, no collector needed). OTLP exporter:   Gateway:Observability:Otlp:Enabled=true    (production, e.g. Jaeger/Grafana).
- (was line 205) OTLP is also auto-enabled when the standard OTEL_EXPORTER_OTLP_ENDPOINT environment variable is set (the OpenTelemetry / .NET Aspire convention), so the gateway "just works" under an Aspire dashboard or any orchestrator that injects that variable — no Gateway__...__Enabled needed.
- (was line 212) Effective endpoint: explicit config wins, otherwise fall back to the SDK's standard env var. When left null we let the OTel SDK resolve OTEL_EXPORTER_OTLP_ENDPOINT itself; we only read it here so auto-enable and the self-tracing filter below know where the collector lives.
- (was line 229) Scope capture + serialization runs on every log record and sits on the per-request path. The gateway's useful context (route_id, path) is stamped straight onto records by the plugins that need it (e.g. body-capture's PerRouteBodyLimitInterceptor via AddParameter), not through the ASP.NET scope stack — so this drops cost, not signal.
- (was line 234) Batch export (the SDK default): spans/logs are queued and flushed in the background instead of a synchronous network call per item — Simple is a dev/debug setting and this export sits on a per-request latency path.

## GatewayServiceCollectionExtensions.ValidateTlsConfiguration

- (was line 296) Fail fast on a route that configures BOTH a client certificate (mTLS) and skipCertificateVerification: the latter selects the "upstream-insecure" HttpClient, which carries no client certificate — so the cert would silently never be presented. The two are mutually exclusive; catch it at startup rather than as a confusing runtime auth failure.

## GatewayServiceCollectionExtensions.ScanPluginDirectory

- (was line 337) External plugins — one subdirectory per route under the plugins root (organizational only; discovery is gateway-wide). DiscoverPluginTypes scans each subdirectory for IPipelinePlugin implementations.
- (was line 353) A rate-limit-store DLL dropped in the plugins root (e.g. ConduitSharp.RateLimit.RedisProtocol) overrides the built-in per-process InMemoryRateLimitStore — last DI registration wins — giving the gateway rate limits shared across replicas without any core change.
- (was line 360) Same seam one level up: a dropped-in IRateLimiter replaces the *algorithm* (e.g. sliding window) rather than the counter backend. The two are independent — a drop-in algorithm may use the registered store, or keep state a store cannot model, as a sliding log does.
- (was line 367) A custom route-matching strategy dropped in as a MatcherPolicy DLL composes with the ones YARP registers — ASP.NET Core collects every registered MatcherPolicy. This is the native replacement for the old drop-in IRouteMatcher seam.
- (was line 374) A load-balancing policy DLL (YARP's ILoadBalancingPolicy) joins the built-in set — a cluster opts in by naming it: "upstream": { "loadBalancingStrategy": "MyPolicy" }.

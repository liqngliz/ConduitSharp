# tests/ConduitSharp.Grafana.E2E.Tests/GrafanaStackE2ETests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GrafanaStackE2ETests.Prometheus_ReceivesGatewayHttpMetrics

- (was line 54) http.server.request.duration comes from AddAspNetCoreInstrumentation and is exported OTLP → collector → Prometheus exporter → scraped by Prometheus.

## GrafanaStackE2ETests.Prometheus_ReceivesConduitSharpsOwnRequestMetrics

- (was line 80) The test above only proves AddAspNetCoreInstrumentation works — it would pass even if every ConduitSharp metric were dead. And they *were*: the YARP re-platforming deleted the middleware that notified IRequestObserver, so OtelMetricsObserver stopped recording and nothing failed. This asserts on the gateway's own instruments (conduitsharp.gateway.requests / .request.duration, via OtelMetricsObserver).
- (was line 86) Matched by regex rather than an exact name: the OTLP → Prometheus exporter mangles names (dots to underscores, unit suffixes, _total on counters), and pinning that mangling would couple the test to the collector's conventions rather than to our instruments.

## GrafanaStackE2ETests.Loki_ReceivesErrorDemoFailureMessage

- (was line 126) Filters on log body text rather than a severity label: Loki's default OTLP→label mapping doesn't promote severity to an indexed stream label, and the formatted message text is stable (IncludeFormattedMessage=true) — matching on it here avoids coupling the test to Loki's internal OTLP ingestion conventions.

## GrafanaStackE2ETests.Loki_ReceivesBothInfoAndErrorLevelGatewayLogs

- (was line 167) "status=200" is literal text in StructuredRequestLogger's rendered message ("... status={StatusCode} ..."), unlike the EventId name ("RequestCompleted"), which never appears in the log body itself.
- (was line 182) /error-demo (seeded in GrafanaStackFixture) hits an unreachable upstream, so the gateway logs a 502 at Error via StructuredRequestLogger's [{RequestId}] ... [error] line.

## GrafanaStackE2ETests.Gateway_ProxiesTraffic_WhileExportingTelemetry

- (was line 202) Sanity: telemetry export must not interfere with the data path.

## GrafanaStackE2ETests.Loki_ReceivesStreamingCapturedBody_AttributedToItsRoute

- (was line 244) The streamOnly upload route carries body-capture-streaming: the tee logs a bounded prefix while YARP streams, and the interceptor stamps the route id into the record. Matching body text AND route id in one record proves capture reached Loki attributed.

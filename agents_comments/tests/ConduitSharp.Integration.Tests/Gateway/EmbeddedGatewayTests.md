# tests/ConduitSharp.Integration.Tests/Gateway/EmbeddedGatewayTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## EmbeddedGatewayTests.Minimal

- (was line 65) The "embed-friendly" baseline: in-memory routes, everything the host might own turned off.

## EmbeddedGatewayTests.PathPrefix_gateway_owns_prefix_and_host_owns_the_rest

- (was line 87) Under the prefix → handled by the gateway (proxied to the upstream).
- (was line 91) Outside the prefix → falls through to the host's own endpoint.

## EmbeddedGatewayTests.Health_endpoints_not_intercepted_when_disabled

- (was line 129) MapHealthEndpoints=false → /healthz is not owned by the gateway, so the catch-all route matches it and forwards upstream instead of answering "OK".

## EmbeddedGatewayTests.Admin_api_not_intercepted_when_disabled

- (was line 143) EnableAdminApi=false → /admin/* is not reserved, so it proxies like any other path (a 200 from the upstream, never a 401 from the admin auth gate).

## EmbeddedGatewayTests.Observability_can_be_enabled_on_an_embedded_gateway

- (was line 158) ConfigureObservability=true wires the OTel providers; enabling the console exporter drives the exporter branches. The gateway must still proxy normally.

## EmbeddedGatewayTests.File_exporter_is_wired_when_enabled

- (was line 187) Drives the file-exporter branch of AddObservability (SimpleActivityExportProcessor + FileSpanExporter) — distinct from the console/OTLP branches.

## EmbeddedGatewayTests.Per_route_mTLS_client_certificate_is_loaded_from_a_pfx_file

- (was line 219) A throwaway self-signed cert stands in for a real client certificate.
- (was line 230) The certificate is keyed by route id, and YARP builds a cluster's HttpMessageInvoker when it loads the config — so starting the gateway is what runs UpstreamForwarderHttpClientFactory.ConfigureHandler and loads the PKCS#12 from disk.

## EmbeddedGatewayTests.Per_route_mTLS_certificate_that_cannot_be_loaded_fails_the_gateway_at_startup

- (was line 255) A broken certificate must not surface as a confusing runtime auth failure on the first request — YARP builds the cluster's client at config load, so it fails the gateway.

## EmbeddedGatewayTests.Per_route_mTLS_certificate_with_neither_path_nor_thumbprint_fails_at_startup

- (was line 273) A client-certificate entry that names a route but supplies neither a PFX path nor a store thumbprint is unusable. It must be rejected when YARP builds the cluster client at config load, not surface as a confusing failure on the first request.

## EmbeddedGatewayTests.Client_cert_plus_dangerousAcceptAnyServerCertificate_on_the_same_route_fails_fast

- (was line 290) Presenting a client certificate to a server you refuse to authenticate defeats the point of mTLS — it is mutual. Startup must reject the combination.

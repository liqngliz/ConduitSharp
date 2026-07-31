namespace ConduitSharp.Grafana.E2E.Tests;

/// <summary>
/// End-to-end assertions that gateway telemetry actually lands in each Grafana-stack
/// backend: traces in Tempo, metrics in Prometheus, logs in Loki. Guards the full
/// OTLP pipeline — gateway exporter → OTel Collector → backend — so a broken
/// collector config, compose wiring, or exporter regression fails loudly here
/// instead of silently producing empty Grafana dashboards.
///
/// The fixture seeds traffic before tests run; each test then polls its backend's
/// query API with a deadline sized for the pipeline's batching (collector: 5s,
/// gateway metric export: 5s via e2e override, Prometheus scrape: 15s).
/// </summary>
[Collection("Grafana E2E")]
[Trait("Category", "E2E")]
public sealed class GrafanaStackE2ETests(GrafanaStackFixture fx)
{
    [Fact]
    public async Task Tempo_ReceivesGatewayTraces()
    {
        if (!fx.DockerAvailable) return;

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"{GrafanaStackFixture.GatewayUrl}/api/inventory"))
        {
            request.Headers.Add("X-Api-Key", GrafanaStackFixture.ApiKey);
            using var response = await fx.Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        var query = Uri.EscapeDataString("""{resource.service.name="ConduitSharp.Gateway"}""");
        var (found, body) = await fx.PollUntilAsync(
            $"{GrafanaStackFixture.TempoUrl}/api/search?q={query}",
            root => root.TryGetProperty("traces", out var traces) &&
                    traces.ValueKind == JsonValueKind.Array &&
                    traces.GetArrayLength() > 0,
            TimeSpan.FromSeconds(60));

        Assert.True(found,
            $"No ConduitSharp.Gateway traces searchable in Tempo within 60s. Last response: {body}");
    }

    [Fact]
    public async Task Prometheus_ReceivesGatewayHttpMetrics()
    {
        if (!fx.DockerAvailable) return;

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"{GrafanaStackFixture.GatewayUrl}/api/inventory"))
        {
            request.Headers.Add("X-Api-Key", GrafanaStackFixture.ApiKey);
            using var response = await fx.Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        var query = Uri.EscapeDataString("sum(http_server_request_duration_seconds_count)");
        var (found, body) = await fx.PollUntilAsync(
            $"{GrafanaStackFixture.PrometheusUrl}/api/v1/query?query={query}",
            root => root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("result", out var result) &&
                    result.GetArrayLength() > 0,
            TimeSpan.FromSeconds(90));

        Assert.True(found,
            $"Gateway HTTP metrics did not reach Prometheus within 90s. Last response: {body}");
    }

    [Fact]
    public async Task Prometheus_ReceivesConduitSharpsOwnRequestMetrics()
    {
        if (!fx.DockerAvailable) return;

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"{GrafanaStackFixture.GatewayUrl}/api/inventory"))
        {
            request.Headers.Add("X-Api-Key", GrafanaStackFixture.ApiKey);
            using var response = await fx.Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        var query = Uri.EscapeDataString("""count({__name__=~"conduitsharp_gateway_request.*"})""");
        var (found, body) = await fx.PollUntilAsync(
            $"{GrafanaStackFixture.PrometheusUrl}/api/v1/query?query={query}",
            root => root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("result", out var result) &&
                    result.GetArrayLength() > 0,
            TimeSpan.FromSeconds(90));

        Assert.True(found,
            "ConduitSharp's own request metrics (conduitsharp.gateway.requests / .request.duration) " +
            $"did not reach Prometheus within 90s — is the IRequestObserver fan-out still wired? Last response: {body}");
    }

    [Fact]
    public async Task Loki_ReceivesGatewayLogs()
    {
        if (!fx.DockerAvailable) return;

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"{GrafanaStackFixture.GatewayUrl}/api/inventory"))
        {
            request.Headers.Add("X-Api-Key", GrafanaStackFixture.ApiKey);
            using var response = await fx.Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        var query = Uri.EscapeDataString("""{service_name="ConduitSharp.Gateway"}""");
        var (found, body) = await fx.PollUntilAsync(
            $"{GrafanaStackFixture.LokiUrl}/loki/api/v1/query_range?query={query}&since=1h",
            root => root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("result", out var result) &&
                    result.GetArrayLength() > 0,
            TimeSpan.FromSeconds(60));

        Assert.True(found,
            $"Gateway logs did not reach Loki within 60s. Last response: {body}");
    }

    [Fact]
    public async Task Loki_ReceivesErrorDemoFailureMessage()
    {
        if (!fx.DockerAvailable) return;

        using var errorResponse = await fx.Client.GetAsync($"{GrafanaStackFixture.GatewayUrl}/error-demo");
        Assert.Equal(HttpStatusCode.BadGateway, errorResponse.StatusCode);

        var query = Uri.EscapeDataString(
            """{service_name="ConduitSharp.Gateway"} |= `route=error-demo` |= `status=502`"""
        );
        var (found, body) = await fx.PollUntilAsync(
            $"{GrafanaStackFixture.LokiUrl}/loki/api/v1/query_range?query={query}&since=1h",
            root => root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("result", out var result) &&
                    result.GetArrayLength() > 0,
            TimeSpan.FromSeconds(60));

        Assert.True(found,
            $"Error-demo failure log did not reach Loki within 60s. Last response: {body}");
    }

    [Fact]
    public async Task Loki_ReceivesBothInfoAndErrorLevelGatewayLogs()
    {
        if (!fx.DockerAvailable) return;

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"{GrafanaStackFixture.GatewayUrl}/api/inventory"))
        {
            request.Headers.Add("X-Api-Key", GrafanaStackFixture.ApiKey);
            using var response = await fx.Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        using var errorResponse = await fx.Client.GetAsync($"{GrafanaStackFixture.GatewayUrl}/error-demo");
        Assert.Equal(HttpStatusCode.BadGateway, errorResponse.StatusCode);

        var infoQuery = Uri.EscapeDataString(
            """{service_name="ConduitSharp.Gateway"} |= `status=200`""");
        var (infoFound, infoBody) = await fx.PollUntilAsync(
            $"{GrafanaStackFixture.LokiUrl}/loki/api/v1/query_range?query={infoQuery}&since=1h",
            root => root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("result", out var result) &&
                    result.GetArrayLength() > 0,
            TimeSpan.FromSeconds(60));

        Assert.True(infoFound,
            $"No Information-level request log reached Loki within 60s. Last response: {infoBody}");

        var errorQuery = Uri.EscapeDataString(
            """{service_name="ConduitSharp.Gateway"} |= `[error]`""");
        var (errorFound, errorBody) = await fx.PollUntilAsync(
            $"{GrafanaStackFixture.LokiUrl}/loki/api/v1/query_range?query={errorQuery}&since=1h",
            root => root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("result", out var result) &&
                    result.GetArrayLength() > 0,
            TimeSpan.FromSeconds(60));

        Assert.True(errorFound,
            $"No Error-level request log reached Loki within 60s. Last response: {errorBody}");
    }

    [Fact]
    public async Task Gateway_ProxiesTraffic_WhileExportingTelemetry()
    {
        if (!fx.DockerAvailable) return;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{GrafanaStackFixture.GatewayUrl}/api/inventory");
        request.Headers.Add("X-Api-Key", GrafanaStackFixture.ApiKey);

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Loki_ReceivesCapturedBody()
    {
        if (!fx.DockerAvailable) return;

        using (var request = new HttpRequestMessage(HttpMethod.Post, $"{GrafanaStackFixture.GatewayUrl}/api/inventory"))
        {
            request.Headers.Add("X-Api-Key", GrafanaStackFixture.ApiKey);
            request.Content = new StringContent("""{"product":"Test","quantity":1}""", System.Text.Encoding.UTF8, "application/json");
            using var response = await fx.Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        var query = Uri.EscapeDataString(
            """{service_name="ConduitSharp.Gateway"} |= `{"product":"Test","quantity":1}`"""
        );
        var (found, body) = await fx.PollUntilAsync(
            $"{GrafanaStackFixture.LokiUrl}/loki/api/v1/query_range?query={query}&since=1h",
            root => root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("result", out var result) &&
                    result.GetArrayLength() > 0,
            TimeSpan.FromSeconds(60));

        Assert.True(found,
            $"Captured body log did not reach Loki within 60s. Last response: {body}");
    }

    [Fact]
    public async Task Loki_ReceivesStreamingCapturedBody_AttributedToItsRoute()
    {
        if (!fx.DockerAvailable) return;

        using (var request = new HttpRequestMessage(HttpMethod.Post, $"{GrafanaStackFixture.GatewayUrl}/api/upload/file"))
        {
            request.Content = new StringContent("""{"upload":"loki-proof"}""", System.Text.Encoding.UTF8, "application/json");
            using var response = await fx.Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        var query = Uri.EscapeDataString(
            """{service_name="ConduitSharp.Gateway"} |= `{"upload":"loki-proof"}` |= `upload-service`"""
        );
        var (found, body) = await fx.PollUntilAsync(
            $"{GrafanaStackFixture.LokiUrl}/loki/api/v1/query_range?query={query}&since=1h",
            root => root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("result", out var result) &&
                    result.GetArrayLength() > 0,
            TimeSpan.FromSeconds(60));

        Assert.True(found,
            $"Streaming captured body did not reach Loki with route attribution within 60s. Last response: {body}");
    }
}

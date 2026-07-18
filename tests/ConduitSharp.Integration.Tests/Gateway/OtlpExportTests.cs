using System.Text;
using ConduitSharp.Integration.Tests.Fixtures;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// In-process OTLP round-trip: boots the gateway with OTLP export enabled against a
/// fake OTLP/HTTP receiver and asserts telemetry actually arrives. Guards the OTel
/// wiring in Program.cs (exporter registration, endpoint resolution, span sources,
/// resource attributes) without requiring Docker or a real collector.
///
/// Uses the standard SDK environment variables (OTEL_EXPORTER_OTLP_ENDPOINT /
/// OTEL_EXPORTER_OTLP_PROTOCOL), which also exercises the gateway's env-var
/// auto-enable path — the same mechanism Aspire and the Docker stacks rely on.
/// Env vars are process-wide, so these tests run serially in their own collection.
/// </summary>
[Collection("OtlpExport")]
[Trait("Category", "Observability")]
public sealed class OtlpExportTests
{
    [CollectionDefinition("OtlpExport", DisableParallelization = true)]
    public sealed class OtlpExportCollection;

    [Fact]
    public async Task GatewayRequest_WithOtlpEnabled_ExportsSpanToCollector()
    {
        await using var collector = await FakeOtlpCollector.StartAsync();

        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", collector.BaseUrl);
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        try
        {
            await using var upstream = await FakeUpstream.StartAsync();
            await using var factory  = await GatewayFactory.CreateAsync(upstream);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/traced");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Spans go through the batch export processor (the SDK default, ~5 s scheduled
            // delay) and the HTTP POST to the collector is async, hence the polling window.
            Assert.True(
                await collector.WaitForPayloadContainingAsync(
                    "/v1/traces", "gateway.request", TimeSpan.FromSeconds(15)),
                $"No OTLP trace export containing a 'gateway.request' span arrived within 15s. " +
                $"Received signals: [{string.Join(", ", collector.Received.Select(kv => $"{kv.Key}×{kv.Value.Count}"))}]. " +
                $"Traces payloads (printable): {string.Join(" ||| ", (collector.Received.GetValueOrDefault("/v1/traces") ?? []).Select(p => new string(Encoding.UTF8.GetString(p).Where(c => c >= ' ' && c < 127).ToArray())))}");

            Assert.True(
                await collector.WaitForPayloadContainingAsync(
                    "/v1/traces", "ConduitSharp.Gateway", TimeSpan.FromSeconds(15)),
                "Trace export did not carry the 'ConduitSharp.Gateway' service resource.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", null);
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", null);
        }
    }

    [Fact]
    public async Task GatewayRequest_WithOtlpEnabled_ExportsLogsToCollector()
    {
        await using var collector = await FakeOtlpCollector.StartAsync();

        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", collector.BaseUrl);
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        try
        {
            await using var upstream = await FakeUpstream.StartAsync();
            await using var factory  = await GatewayFactory.CreateAsync(upstream);
            using var client = factory.CreateClient();

            await client.GetAsync("/api/traced");

            Assert.True(
                await collector.WaitForPayloadContainingAsync(
                    "/v1/logs", "ConduitSharp.Gateway", TimeSpan.FromSeconds(15)),
                "No OTLP log export from the gateway arrived within 15s.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", null);
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", null);
        }
    }
}

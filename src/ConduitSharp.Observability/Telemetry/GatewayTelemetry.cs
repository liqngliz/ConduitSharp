using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ConduitSharp.Observability.Telemetry;

/// <summary>
/// Central home for the gateway's OpenTelemetry instrumentation primitives.
/// Both <see cref="ActivitySource"/> and <see cref="Meter"/> use the same source name
/// so a single <c>AddSource</c> / <c>AddMeter</c> call in the host wires everything up.
/// </summary>
public static class GatewayTelemetry
{
    /// <summary>Source name used for both tracing and metrics — pass to AddSource/AddMeter in the host.</summary>
    public const string SourceName = "ConduitSharp.Gateway";

    /// <summary>Emits one span per gateway request.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "0.1.0");

    private static readonly Meter Meter = new(SourceName, "0.1.0");

    /// <summary>Total requests processed, tagged by route_id / http.request.method / http.response.status_code.</summary>
    public static readonly Counter<long> RequestCounter =
        Meter.CreateCounter<long>(
            "conduitsharp.gateway.requests",
            unit: "{request}",
            description: "Total requests processed by the gateway.");

    /// <summary>End-to-end request duration in milliseconds.</summary>
    public static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>(
            "conduitsharp.gateway.request.duration",
            unit: "ms",
            description: "End-to-end gateway request duration.");

    /// <summary>Total 5xx responses.</summary>
    public static readonly Counter<long> ErrorCounter =
        Meter.CreateCounter<long>(
            "conduitsharp.gateway.errors",
            unit: "{request}",
            description: "Total gateway requests that resulted in a 5xx response.");

    /// <summary>Total successful admin route reloads.</summary>
    public static readonly Counter<long> AdminReloadCounter =
        Meter.CreateCounter<long>(
            "conduitsharp.gateway.admin.reloads",
            unit: "{reload}",
            description: "Total successful admin route reloads.");
}

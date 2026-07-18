using System.Diagnostics;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Observability.Telemetry;

namespace ConduitSharp.Observability.Metrics;

/// <summary>
/// Records per-request counters and duration to the gateway's OpenTelemetry <see cref="Meter"/>.
/// Register alongside other <c>IRequestObserver</c> implementations in DI (e.g.
/// <see cref="ConduitSharp.Observability.Logging.StructuredRequestLogger"/>).
/// When no OTel listener is configured the instruments are no-ops — zero overhead.
/// </summary>
public sealed class OtelMetricsObserver : IRequestObserver
{
    public void OnRequestCompleted(RequestObservation observation)
    {
        var tags = new TagList
        {
            { "route_id",                 observation.RouteId ?? "unmatched" },
            { "http.request.method",      observation.Method },
            { "http.response.status_code", observation.StatusCode },
        };

        GatewayTelemetry.RequestCounter.Add(1, tags);
        GatewayTelemetry.RequestDuration.Record(observation.DurationMs, tags);

        if (observation.StatusCode >= 500)
            GatewayTelemetry.ErrorCounter.Add(1, tags);
    }
}

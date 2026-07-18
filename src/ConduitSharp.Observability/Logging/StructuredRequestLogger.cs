using ConduitSharp.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace ConduitSharp.Observability.Logging;

/// <summary>
/// Emits a structured log line for every completed request.
/// Register as <c>IRequestObserver</c> in DI to activate:
/// <c>services.AddSingleton&lt;IRequestObserver, StructuredRequestLogger&gt;()</c>
/// </summary>
public sealed class StructuredRequestLogger(ILogger<StructuredRequestLogger> logger) : IRequestObserver
{
    private static readonly Action<ILogger, string, string, string, string?, int, long, Exception?> _logCompleted =
        LoggerMessage.Define<string, string, string, string?, int, long>(
            LogLevel.Information,
            new EventId(100, "RequestCompleted"),
            "[{RequestId}] {Method} {Path} → route={RouteId} status={StatusCode} ({DurationMs}ms)");

    private static readonly Action<ILogger, string, string, string, string?, int, long, Exception?> _logError =
        LoggerMessage.Define<string, string, string, string?, int, long>(
            LogLevel.Error,
            new EventId(101, "RequestError"),
            "[{RequestId}] {Method} {Path} → route={RouteId} status={StatusCode} ({DurationMs}ms) [error]");

    public void OnRequestCompleted(RequestObservation observation)
    {
        var log = observation.StatusCode >= 500 ? _logError : _logCompleted;
        log(logger,
            observation.RequestId,
            observation.Method,
            observation.Path,
            observation.RouteId,
            observation.StatusCode,
            observation.DurationMs,
            null);
    }
}

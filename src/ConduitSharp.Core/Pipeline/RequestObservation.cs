namespace ConduitSharp.Core.Pipeline;

/// <summary>
/// Immutable snapshot of a completed request passed to every <see cref="IRequestObserver"/>.
/// </summary>
public sealed record RequestObservation(
    string RequestId,
    string Method,
    string Path,
    string? RouteId,
    int StatusCode,
    long DurationMs);

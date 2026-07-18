namespace ConduitSharp.Core.Pipeline;

/// <summary>
/// Receives a notification after every request completes (all code paths: 404,
/// plugin short-circuit, upstream success, and upstream errors). Implementations are
/// registered in DI as <see cref="IRequestObserver"/> singletons and notified from the
/// gateway's outermost middleware in a finally block, so timing is always captured.
/// </summary>
public interface IRequestObserver
{
    /// <summary>Called once per request after the response has been written, on all code paths.</summary>
    void OnRequestCompleted(RequestObservation observation);
}

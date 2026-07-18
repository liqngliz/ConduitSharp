namespace ConduitSharp.Traffic.RateLimiting;

/// <summary>
/// The rate-limit algorithm: decides whether a request identified by <c>key</c> may proceed and,
/// when it may not, how long the caller should wait.
///
/// Swap the algorithm by dropping an assembly that implements this into the plugins directory —
/// the same discovery <see cref="IRateLimitStore"/> uses. The built-in
/// <see cref="FixedWindowRateLimiter"/> is used otherwise. See the SlidingWindow example.
///
/// This is the *algorithm*; <see cref="IRateLimitStore"/> is the *counter backend* a fixed-window
/// algorithm keeps its counts in. They are separate because the reason to change them differs: a
/// store changes to share counters across replicas (Redis), an algorithm changes what "over the
/// limit" means. An algorithm need not use a store at all — a sliding log keeps timestamps, which
/// no store models.
///
/// Implementations are singletons and must be thread-safe: the window and quota arrive per call
/// rather than per instance, so one limiter serves every route.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to acquire a permit for <paramref name="key"/> under a quota of
    /// <paramref name="maxRequests"/> per <paramref name="windowSeconds"/>.
    /// </summary>
    RateLimitDecision TryAcquire(string key, int windowSeconds, long maxRequests);
}

/// <summary>
/// The outcome of a rate-limit check, including the <c>Retry-After</c> the caller should be told.
///
/// The retry hint belongs to the algorithm, not its caller: a fixed window rolls over at a
/// predictable boundary, a sliding log frees a permit when its oldest request ages out, and a
/// token bucket refills at its own rate. Only the algorithm knows which — a bare <c>bool</c>
/// forces the caller to re-implement one algorithm's arithmetic and get every other one wrong.
/// </summary>
/// <param name="Allowed">Whether the request may proceed.</param>
/// <param name="RetryAfterSeconds">
/// Seconds until a permit is expected to free up; zero when allowed. Floored at 1 when denied —
/// <c>Retry-After: 0</c> invites an instant retry storm.
/// </param>
public readonly record struct RateLimitDecision(bool Allowed, int RetryAfterSeconds)
{
    /// <summary>The request may proceed.</summary>
    public static RateLimitDecision Allow => new(true, 0);

    /// <summary>Over quota; retry in <paramref name="retryAfterSeconds"/> (floored at 1).</summary>
    public static RateLimitDecision Deny(long retryAfterSeconds) =>
        new(false, (int)Math.Max(1, retryAfterSeconds));
}

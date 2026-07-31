using System.Collections.Concurrent;
using ConduitSharp.Traffic.RateLimiting;

namespace ConduitSharp.RateLimit.SlidingWindow;

/// <summary>
/// Sliding-log rate limiter: a drop-in <see cref="IRateLimiter"/> replacing the built-in
/// fixed-window algorithm. It keeps the timestamp of every permit still inside the window, so the
/// limit holds at every instant rather than only at aligned boundaries.
///
/// <para>State is per-process, so quotas are per replica.</para>
/// </summary>
public sealed class SlidingWindowRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<long>> _log = new();
    private readonly Func<long> _nowMillis;

    /// <summary>Uses the wall clock.</summary>
    public SlidingWindowRateLimiter() : this(() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { }

    /// <summary>Test seam: supply a deterministic clock in Unix milliseconds.</summary>
    public SlidingWindowRateLimiter(Func<long> clockMillis) => _nowMillis = clockMillis;

    /// <inheritdoc />
    public RateLimitDecision TryAcquire(string key, int windowSeconds, long maxRequests)
    {
        var now = _nowMillis();
        var windowMillis = windowSeconds * 1000L;
        var cutoff = now - windowMillis;
        var log = _log.GetOrAdd(key, _ => new Queue<long>());

        lock (log)
        {
            while (log.Count > 0 && log.Peek() <= cutoff)
                log.Dequeue();

            if (log.Count < maxRequests)
            {
                log.Enqueue(now);
                return RateLimitDecision.Allow;
            }

            var freesAtMillis = log.Peek() + windowMillis;
            return RateLimitDecision.Deny((freesAtMillis - now + 999) / 1000);
        }
    }

    /// <summary>Exposed for testing — number of keys currently holding timestamps.</summary>
    internal int TrackedKeys => _log.Count;
}

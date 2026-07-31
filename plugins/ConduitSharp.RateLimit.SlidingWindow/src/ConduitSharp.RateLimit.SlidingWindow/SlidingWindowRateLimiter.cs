using System.Collections.Concurrent;
using ConduitSharp.Traffic.RateLimiting;

namespace ConduitSharp.RateLimit.SlidingWindow;

/// <summary>
/// Sliding-log rate limiter: a drop-in <see cref="IRateLimiter"/> that replaces the built-in
/// fixed-window algorithm.
///
/// Fixed windows are cheap but bursty at the seam — a caller can spend a full quota just before a
/// boundary and another just after, delivering up to 2x the nominal rate across it. This keeps the
/// timestamp of every permit still inside the window instead, so the limit holds across *every*
/// instant, not just the aligned ones. The trade is memory: O(maxRequests) timestamps per active
/// key against the fixed window's single counter. Worth it for expensive endpoints where the burst
/// is the thing you are actually paying for; not worth it for a coarse per-minute quota.
///
/// It deliberately does not use <see cref="IRateLimitStore"/>: that interface counts hits per
/// aligned window id, which is exactly the model a sliding log rejects. An algorithm is free to
/// keep state a store cannot express — which is why the algorithm and the store are separate seams.
/// State is per-process, so quotas are per replica.
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

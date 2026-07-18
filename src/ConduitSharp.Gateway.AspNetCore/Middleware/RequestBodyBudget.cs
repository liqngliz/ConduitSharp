namespace ConduitSharp.Gateway.Middleware;

/// <summary>
/// Tracks bytes currently buffered across all in-flight request bodies, in two tiers so the gateway
/// degrades in steps instead of falling off a cliff:
///
///   • <b>memory</b> (<c>Gateway:RequestLimits:MaxMemoryBufferedBodyBytes</c>) — while this has
///     headroom a body buffers in RAM, which is ~3–5x faster than spilling to disk.
///   • <b>total</b> (<c>Gateway:RequestLimits:MaxTotalBufferedBodyBytes</c>) — RAM and spilled bytes
///     combined. Once the memory tier is full, bodies are still served from disk until this runs
///     out; only then does the gateway shed with a 503.
///
/// Total reservations are made chunk-by-chunk as a body is read, and released when the request
/// completes. The memory reservation is different: it covers the buffer's *capacity*, not its fill,
/// because <c>FileBufferingReadStream</c> rents the whole threshold from <c>ArrayPool</c> at
/// construction and returns it the instant it spills. So a caller reserves the threshold up front
/// and releases it on the spill — at which point the bytes are still counted against the total, only
/// the tier they occupy has changed.
///
/// A non-positive limit disables that tier, but the two disable in opposite directions: no total
/// limit means unlimited buffering, while no memory limit means no memory tier at all — every body
/// spills.
/// </summary>
internal sealed class RequestBodyBudget(long maxTotalBytes, long maxMemoryBytes)
{
    private long _used;
    private long _memoryUsed;

    /// <summary>Combined RAM + spilled ceiling. Exceeding it is the 503 load-shed.</summary>
    public long MaxTotalBytes { get; } = maxTotalBytes;

    /// <summary>RAM-resident ceiling, carved out of <see cref="MaxTotalBytes"/>.</summary>
    public long MaxMemoryBytes { get; } = maxMemoryBytes;

    /// <summary>
    /// RAM the memory tier can still hand out. A sizing hint only — it is read before reserving, so
    /// it may be stale by the time <see cref="TryReserveMemory"/> runs. That race is benign: the
    /// reserve itself is atomic and simply fails, dropping that request to the disk tier.
    /// </summary>
    public long MemoryHeadroom =>
        MaxMemoryBytes <= 0 ? 0 : Math.Max(0, MaxMemoryBytes - Interlocked.Read(ref _memoryUsed));

    /// <summary>Reserves against the combined ceiling. False is the 503.</summary>
    public bool TryReserve(long bytes)
    {
        if (MaxTotalBytes <= 0) return true; // no limit configured — buffering is untracked
        return TryAdd(ref _used, MaxTotalBytes, bytes);
    }

    public void Release(long bytes)
    {
        if (MaxTotalBytes <= 0 || bytes == 0) return;
        Interlocked.Add(ref _used, -bytes);
    }

    /// <summary>
    /// Reserves RAM for a body's buffer capacity. False means "no headroom — spill this one",
    /// which is a normal outcome, not an error. A non-positive limit disables the memory tier, so
    /// this always fails and every body spills.
    /// </summary>
    public bool TryReserveMemory(long bytes)
    {
        if (MaxMemoryBytes <= 0) return false; // memory tier disabled — spill everything
        return TryAdd(ref _memoryUsed, MaxMemoryBytes, bytes);
    }

    /// <summary>
    /// Releases a memory reservation — on request completion, or the moment the body spills and
    /// <c>FileBufferingReadStream</c> hands its rented buffer back to the pool.
    /// </summary>
    public void ReleaseMemory(long bytes)
    {
        if (MaxMemoryBytes <= 0 || bytes == 0) return;
        Interlocked.Add(ref _memoryUsed, -bytes);
    }

    private static bool TryAdd(ref long counter, long max, long bytes)
    {
        while (true)
        {
            var current = Interlocked.Read(ref counter);
            var next    = current + bytes;
            if (next > max) return false;
            if (Interlocked.CompareExchange(ref counter, next, current) == current) return true;
        }
    }
}

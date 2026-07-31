namespace ConduitSharp.Gateway.Middleware;

/// <summary>
/// Tracks what buffered request bodies are consuming, as <b>two independent budgets — one per
/// physical resource</b>:
///
///   • <b>RAM</b> (<c>Gateway:RequestLimits:MaxRamBufferedBodyBytes</c>) — heap held by buffered
///     bodies and by body-capture prefixes. Bounded by the memory available to the process.
///   • <b>disk</b> (<c>Gateway:RequestLimits:MaxDiskBufferedBodyBytes</c>) — bytes in spill files.
///     Bounded by free space on <c>SpillDirectory</c>.
///
/// They are deliberately not one combined number. A single "RAM + disk" total cannot be sized
/// correctly: raising it to suit a large spill volume would silently license the same growth in RAM,
/// which is how the process gets OOM-killed instead of shedding.
///
/// <b>How a body moves between them.</b> The RAM reservation covers the buffer's *capacity*, not its
/// fill, because <c>FileBufferingReadStream</c> rents the whole threshold from <c>ArrayPool</c> at
/// construction and returns it the instant it spills. So a caller reserves the threshold up front;
/// if the body outgrows it, the caller releases the RAM reservation and starts charging the bytes to
/// the disk budget instead. Disk reservations are made chunk-by-chunk as the body is read and
/// released when the request completes.
///
/// <b>What refusal means.</b> Failing the RAM reservation is routine — that body spills. Failing the
/// disk reservation is the 503 load-shed: neither resource has room.
///
/// <c>0</c> on either budget means "none of that resource is available" — no RAM means every body
/// spills; no disk means a body must fit RAM or be shed. Negative values are rejected at startup.
/// </summary>
internal sealed class RequestBodyBudget(long maxRamBytes, long maxDiskBytes)
{
    private long _ramUsed;
    private long _diskUsed;

    /// <summary>Ceiling on RAM held by buffered bodies and capture prefixes.</summary>
    public long MaxRamBytes { get; } = maxRamBytes;

    /// <summary>Ceiling on bytes resident in spill files. Exhausting it is the 503 load-shed.</summary>
    public long MaxDiskBytes { get; } = maxDiskBytes;

    /// <summary>
    /// RAM the budget can still hand out. A sizing hint only — it is read before reserving, so it may
    /// be stale by the time <see cref="TryReserveRam"/> runs. That race is benign: the reserve itself
    /// is atomic and simply fails, dropping that request to the disk tier.
    /// </summary>
    public long RamHeadroom =>
        MaxRamBytes <= 0 ? 0 : Math.Max(0, MaxRamBytes - Interlocked.Read(ref _ramUsed));

    /// <summary>
    /// Reserves RAM — for a body's buffer capacity, or for a capture prefix. False means "no RAM
    /// headroom". For a buffered body that is a normal outcome (spill it); for capture, which has no
    /// disk path, it is the 503.
    /// </summary>
    public bool TryReserveRam(long bytes)
    {
        if (bytes <= 0) return true;
        if (MaxRamBytes <= 0) return false;
        return TryAdd(ref _ramUsed, MaxRamBytes, bytes);
    }

    /// <summary>
    /// Releases a RAM reservation — on request completion, or the moment the body spills and
    /// <c>FileBufferingReadStream</c> hands its rented buffer back to the pool.
    /// </summary>
    public void ReleaseRam(long bytes)
    {
        if (MaxRamBytes <= 0 || bytes <= 0) return;
        Interlocked.Add(ref _ramUsed, -bytes);
    }

    /// <summary>Reserves spill-file bytes. False is the 503: the body fits in neither budget.</summary>
    public bool TryReserveDisk(long bytes)
    {
        if (bytes <= 0) return true;
        if (MaxDiskBytes <= 0) return false;
        return TryAdd(ref _diskUsed, MaxDiskBytes, bytes);
    }

    public void ReleaseDisk(long bytes)
    {
        if (MaxDiskBytes <= 0 || bytes <= 0) return;
        Interlocked.Add(ref _diskUsed, -bytes);
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

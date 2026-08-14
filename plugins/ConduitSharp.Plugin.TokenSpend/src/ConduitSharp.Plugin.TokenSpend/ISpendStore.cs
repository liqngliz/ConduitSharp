namespace ConduitSharp.Plugin.TokenSpend;

/// <summary>
/// Durable history of what LLM calls cost. The gateway writes through <see cref="Add"/> on the
/// request path, so it must never block; anything that reads the history uses <see cref="Read"/>.
///
/// <para>This is the seam a different backend swaps in on. <see cref="JsonlSpendStore"/> is the
/// shipped implementation; a SQLite or Postgres one implements the same three methods and is
/// registered in its place, exactly as <c>IRateLimitStore</c> and <c>ICacheService</c> work.</para>
/// </summary>
public interface ISpendStore
{
    /// <summary>Queues a row. Returns immediately: never blocks the request, and drops the row
    /// rather than stalling a caller if the queue is full.</summary>
    void Add(SpendRecord record);

    /// <summary>Every row with a timestamp in <paramref name="from"/>..<paramref name="to"/> inclusive.</summary>
    IReadOnlyList<SpendRecord> Read(DateTimeOffset from, DateTimeOffset to);

    /// <summary>
    /// Turns a raw caller credential into the stable, non-reversible id written to disk.
    ///
    /// <para>This exists so the raw value has exactly one place it can be handled. The header this
    /// is fed is <c>x-api-key</c> or <c>Authorization</c>, which on a provider route carries the
    /// user's real API key. Persisting that verbatim would turn a spend log into a credential
    /// file, so nothing but the return value of this method is ever stored.</para>
    /// </summary>
    string HashCaller(string rawKey);
}

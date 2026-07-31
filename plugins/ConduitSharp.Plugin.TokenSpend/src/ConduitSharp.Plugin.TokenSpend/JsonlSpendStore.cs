using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace ConduitSharp.Plugin.TokenSpend;

/// <summary>
/// Spend history as one JSON object per line, in one file per UTC day
/// (<c>spend-2026-07-31.jsonl</c>), under <c>~/.conduit-spend</c> by default.
///
/// <para>Writes go through a bounded channel drained by a single background task, the same shape
/// <c>BodyCaptureToFilePlugin</c> uses, so a slow disk costs the request nothing. One deliberate
/// difference from that plugin: it rolls with a single <c>.1</c> backup and therefore deletes
/// history, which a spend log must never do. Day files are never rewritten and never removed.</para>
///
/// <para>JSONL rather than SQLite: no native library to resolve, no schema migration, and the file
/// stays greppable. Reading a range costs one pass over the day files it covers, which at a few
/// hundred bytes a row is nothing for a personal history. <see cref="ISpendStore"/> is where a real
/// database swaps in if that stops being true.</para>
/// </summary>
public sealed class JsonlSpendStore : ISpendStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;
    private readonly byte[] _salt;
    private readonly Channel<SpendRecord> _queue;
    private readonly Task _drain;

    /// <param name="directory">Where day files live. Defaults to the <c>CONDUIT_SPEND_DATA</c>
    /// environment variable, then <c>~/.conduit-spend</c>.</param>
    /// <param name="capacity">Rows that may be queued before new ones are dropped.</param>
    public JsonlSpendStore(string? directory = null, int capacity = 1024)
    {
        _directory = directory
            ?? Environment.GetEnvironmentVariable("CONDUIT_SPEND_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".conduit-spend");
        Directory.CreateDirectory(_directory);
        _salt = LoadOrCreateSalt(_directory);

        _queue = Channel.CreateBounded<SpendRecord>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
        _drain = Task.Run(DrainAsync);
    }

    public void Add(SpendRecord record) => _queue.Writer.TryWrite(record);

    public IReadOnlyList<SpendRecord> Read(DateTimeOffset from, DateTimeOffset to)
    {
        var rows = new List<SpendRecord>();
        for (var day = from.UtcDateTime.Date; day <= to.UtcDateTime.Date; day = day.AddDays(1))
        {
            var path = PathForDay(day);
            if (!File.Exists(path))
                continue;

            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0)
                    continue;
                SpendRecord? row;
                try
                {
                    row = JsonSerializer.Deserialize<SpendRecord>(line, Json);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (row is not null && row.Timestamp >= from && row.Timestamp <= to)
                    rows.Add(row);
            }
        }
        return rows;
    }

    public string HashCaller(string rawKey)
    {
        ArgumentNullException.ThrowIfNull(rawKey);
        var mac = HMACSHA256.HashData(_salt, Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(mac.AsSpan(0, 12)).ToLowerInvariant();
    }

    private async Task DrainAsync()
    {
        await foreach (var record in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await File.AppendAllTextAsync(
                    PathForDay(record.Timestamp.UtcDateTime.Date),
                    JsonSerializer.Serialize(record, Json) + Environment.NewLine);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string PathForDay(DateTime utcDay) =>
        Path.Combine(_directory, $"spend-{utcDay:yyyy-MM-dd}.jsonl");

    private static byte[] LoadOrCreateSalt(string directory)
    {
        var path = Path.Combine(directory, "salt");
        if (!File.Exists(path))
            File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
        return File.ReadAllBytes(path);
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try
        {
            _drain.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }
    }
}

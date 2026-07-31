namespace ConduitSharp.Plugin.TokenSpend.Tests;

public sealed class JsonlSpendStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "conduit-spend-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static SpendRecord Row(DateTimeOffset at, long input = 1, string caller = "c") => new()
    {
        Timestamp = at,
        Route = "route-a",
        Model = "claude-opus-4-6",
        Caller = caller,
        InputTokens = input,
        OutputTokens = 2,
    };

    [Fact]
    public void RowsSurviveRestart_AcrossDayFiles_AndTheRangeIsRespected()
    {
        var day1 = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        var day3 = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        using (var writer = new JsonlSpendStore(_dir))
        {
            writer.Add(Row(day1, input: 10));
            writer.Add(Row(day2, input: 20));
            writer.Add(Row(day3, input: 30));
        }   // Dispose drains the queue

        // A second instance proves the history is on disk, not in the first store's memory.
        using var reader = new JsonlSpendStore(_dir);

        var all = reader.Read(day1, day3);
        Assert.Equal(3, all.Count);
        Assert.Equal([10, 20, 30], all.Select(r => r.InputTokens).Order());

        var middle = reader.Read(day2.AddHours(-1), day2.AddHours(1));
        Assert.Equal(20, Assert.Single(middle).InputTokens);

        Assert.True(File.Exists(Path.Combine(_dir, "spend-2026-07-30.jsonl")));
        Assert.True(File.Exists(Path.Combine(_dir, "spend-2026-08-01.jsonl")));
    }

    [Fact]
    public void EveryFieldRoundTrips()
    {
        var at = new DateTimeOffset(2026, 7, 31, 9, 30, 0, TimeSpan.Zero);
        var original = new SpendRecord
        {
            Timestamp = at,
            Route = "anthropic",
            Model = "claude-opus-4-6",
            Caller = "abc123",
            InputTokens = 12,
            OutputTokens = 300,
            CacheWriteTokens = 9000,
            CacheReadTokens = 54000,
            SessionId = "deadbeef",
            TurnIndex = 34,
            ToolUseCount = 7,
            DurationMs = 1450,
            Streamed = true,
            PromptPrefix = "continue",
        };

        using (var writer = new JsonlSpendStore(_dir))
            writer.Add(original);

        using var reader = new JsonlSpendStore(_dir);
        Assert.Equal(original, Assert.Single(reader.Read(at.AddMinutes(-1), at.AddMinutes(1))));
    }

    [Fact]
    public void CallerHashIsStableAcrossRestarts_AndDistinctPerKey()
    {
        string first, second, other;
        using (var store = new JsonlSpendStore(_dir))
        {
            first = store.HashCaller("sk-ant-alice");
            other = store.HashCaller("sk-ant-bob");
        }

        // The salt is persisted, so yesterday's rows still group with today's.
        using (var restarted = new JsonlSpendStore(_dir))
            second = restarted.HashCaller("sk-ant-alice");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.DoesNotContain("alice", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADifferentSalt_ProducesADifferentHashForTheSameKey()
    {
        var otherDir = _dir + "-b";
        try
        {
            using var a = new JsonlSpendStore(_dir);
            using var b = new JsonlSpendStore(otherDir);
            Assert.NotEqual(a.HashCaller("sk-ant-alice"), b.HashCaller("sk-ant-alice"));
        }
        finally
        {
            Directory.Delete(otherDir, recursive: true);
        }
    }

    [Fact]
    public void ARawApiKeyNeverReachesDisk()
    {
        const string apiKey = "sk-ant-api03-REAL-CREDENTIAL";
        var at = DateTimeOffset.UtcNow;

        using (var store = new JsonlSpendStore(_dir))
            store.Add(Row(at, caller: store.HashCaller(apiKey)));

        foreach (var file in Directory.GetFiles(_dir))
            Assert.DoesNotContain(apiKey, File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void ATornLineIsSkipped_RatherThanLosingTheDay()
    {
        var at = new DateTimeOffset(2026, 7, 31, 9, 30, 0, TimeSpan.Zero);
        using (var store = new JsonlSpendStore(_dir))
            store.Add(Row(at, input: 42));

        // Simulate a crash mid-write: a partial JSON object appended after a good row.
        File.AppendAllText(Path.Combine(_dir, "spend-2026-07-31.jsonl"), "{\"ts\":\"2026-07-31T09:3" + Environment.NewLine);

        using var reader = new JsonlSpendStore(_dir);
        Assert.Equal(42, Assert.Single(reader.Read(at.AddHours(-1), at.AddHours(1))).InputTokens);
    }
}

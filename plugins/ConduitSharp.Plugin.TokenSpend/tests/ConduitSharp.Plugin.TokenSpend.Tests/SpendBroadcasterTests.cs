using System.Threading.Channels;
using Xunit;

namespace ConduitSharp.Plugin.TokenSpend.Tests;

public class SpendBroadcasterTests
{
    [Fact]
    public async Task Subscribe_ReceivesPublishedRecords()
    {
        var broadcaster = new SpendBroadcaster();
        var subId = broadcaster.Subscribe(out var reader);

        var record = new SpendRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Route = "claude",
            InputTokens = 100,
            OutputTokens = 50,
        };

        broadcaster.Publish(record);

        var received = await reader.ReadAsync();
        Assert.Equal("claude", received.Route);
        Assert.Equal(100, received.InputTokens);
        Assert.Equal(50, received.OutputTokens);

        broadcaster.Unsubscribe(subId);
    }

    [Fact]
    public async Task Unsubscribe_CompletesChannelWriter()
    {
        var broadcaster = new SpendBroadcaster();
        var subId = broadcaster.Subscribe(out var reader);

        broadcaster.Unsubscribe(subId);

        var hasItem = await reader.WaitToReadAsync();
        Assert.False(hasItem);
    }

    [Fact]
    public async Task Publish_FannedOutToMultipleSubscribers()
    {
        var broadcaster = new SpendBroadcaster();
        var sub1 = broadcaster.Subscribe(out var reader1);
        var sub2 = broadcaster.Subscribe(out var reader2);

        var record = new SpendRecord { Route = "codex", InputTokens = 200 };
        broadcaster.Publish(record);

        var rec1 = await reader1.ReadAsync();
        var rec2 = await reader2.ReadAsync();

        Assert.Equal("codex", rec1.Route);
        Assert.Equal("codex", rec2.Route);

        broadcaster.Unsubscribe(sub1);
        broadcaster.Unsubscribe(sub2);
    }
}

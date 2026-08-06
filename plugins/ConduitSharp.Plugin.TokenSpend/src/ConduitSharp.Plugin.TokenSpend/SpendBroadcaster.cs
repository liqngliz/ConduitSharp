using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ConduitSharp.Plugin.TokenSpend;

/// <summary>
/// In-memory pub/sub broadcaster for real-time <see cref="SpendRecord"/> events.
/// Decoupled from durable storage (<see cref="ISpendStore"/>): callers publish on the request path,
/// and SSE endpoint subscribers receive rows without disk IO latency.
/// </summary>
public sealed class SpendBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<SpendRecord>> _subscribers = new();

    /// <summary>
    /// Subscribes a new client to real-time spend records.
    /// </summary>
    /// <param name="reader">The channel reader to read emitted spend records from.</param>
    /// <returns>A unique subscription ID to pass to <see cref="Unsubscribe"/> when done.</returns>
    public Guid Subscribe(out ChannelReader<SpendRecord> reader)
    {
        var channel = Channel.CreateBounded<SpendRecord>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        reader = channel.Reader;
        return id;
    }

    /// <summary>
    /// Unsubscribes a client and completes its channel writer.
    /// </summary>
    /// <param name="id">The subscription ID returned by <see cref="Subscribe"/>.</param>
    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Publishes a record to all current subscribers.
    /// Uses bounded channels with DropWrite so a stalled reader never blocks the caller or other subscribers.
    /// </summary>
    public void Publish(SpendRecord record)
    {
        foreach (var (_, channel) in _subscribers)
        {
            channel.Writer.TryWrite(record);
        }
    }
}

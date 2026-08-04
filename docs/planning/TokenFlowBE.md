# Real-Time Token Spend Streaming: Backend Plan

**Assigned Agent / Model**: Claude Opus 5
**Goal**: Expose an SSE (Server-Sent Events) endpoint `GET /api/spend/stream` in the backend so incoming spend records are broadcast in real time as they are written.

---

## Technical Design

1. **Broadcaster, separate from the store**:
   - New `SpendBroadcaster` (singleton, `plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/SpendBroadcaster.cs`).
   - `ISpendStore` and `JsonlSpendStore` are untouched. Storage durability and live push are different
     concerns: a future SQLite/Postgres `ISpendStore` should not be forced to also carry broadcast
     logic, and `JsonlSpendStore`'s existing docs already commit it to "durable write, never blocks."
   - Shape:
     ```csharp
     public sealed class SpendBroadcaster
     {
         private readonly ConcurrentDictionary<Guid, Channel<SpendRecord>> _subscribers = new();

         public Guid Subscribe(out ChannelReader<SpendRecord> reader)
         {
             var channel = Channel.CreateBounded<SpendRecord>(new BoundedChannelOptions(64)
             {
                 FullMode = BoundedChannelFullMode.DropWrite,
                 SingleReader = true,
                 SingleWriter = true,
             });
             var id = Guid.NewGuid();
             _subscribers[id] = channel;
             reader = channel.Reader;
             return id;
         }

         public void Unsubscribe(Guid id)
         {
             if (_subscribers.TryRemove(id, out var channel))
                 channel.Writer.TryComplete();
         }

         public void Publish(SpendRecord record)
         {
             foreach (var (_, channel) in _subscribers)
                 channel.Writer.TryWrite(record);   // bounded + DropWrite: a stalled client never blocks a publish
         }
     }
     ```
   - `TryWrite` on a bounded `DropWrite` channel never throws and never blocks, so `Publish` needs no
     try/catch per subscriber — a slow reader just misses rows until it drains, same drop contract
     `JsonlSpendStore`'s own queue already uses.

2. **Wired from the write path, not the drain path**:
   - In `TokenSpendPlugin.ExecuteAsync`, right after `store.Add(record)`, add
     `broadcaster.Publish(record)`. Firing here (not inside `JsonlSpendStore`'s background
     `DrainAsync`) keeps the SSE feed on the request path's timing, not the disk queue's — a
     subscriber sees a row the moment it's charged, not after it lands on disk.
   - `SpendBroadcaster` resolved via `context.RequestServices`, same as `ISpendStore` today.

3. **SSE Streaming Endpoint** (`Program.cs`):
   - `app.MapGet("/api/spend/stream", async (HttpContext ctx, SpendBroadcaster broadcaster) => { ... })`,
     alongside the existing `/api/spend` and `/api/spend/{date}` endpoints (`Program.cs:28,33`).
   - `ctx.Response.Headers.ContentType = "text/event-stream"`, plus `Cache-Control: no-cache` and
     `X-Accel-Buffering: no` (stops nginx-shaped reverse proxies from buffering the stream).
   - `var id = broadcaster.Subscribe(out var reader);` then `try { ... } finally { broadcaster.Unsubscribe(id); }`.
   - Loop:
     ```csharp
     while (!ctx.RequestAborted.IsCancellationRequested)
     {
         using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
         using var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, timeout.Token);
         SpendRecord record;
         try
         {
             record = await reader.ReadAsync(linked.Token);
         }
         catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
         {
             break;   // client disconnected — expected, not an error
         }
         catch (OperationCanceledException)
         {
             await ctx.Response.WriteAsync(": keepalive\n\n", ctx.RequestAborted);
             await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
             continue;   // 15s idle tick, keeps proxies from killing the connection
         }
         await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(record)}\n\n", ctx.RequestAborted);
         await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
     }
     ```
   - Two trailing newlines on every `data:` frame — that blank line is what terminates an SSE event,
     not decoration.

---

## File Changes Overview

### 1. `plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/SpendBroadcaster.cs` (new)
- `Subscribe` / `Unsubscribe` / `Publish` over a `ConcurrentDictionary<Guid, Channel<SpendRecord>>`.

### 2. `plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/TokenSpendPlugin.cs`
- After `store.Add(record)`: resolve `SpendBroadcaster` and call `Publish(record)`.

### 3. `examples/ConduitSharp.Spend/Program.cs`
- `builder.Services.AddSingleton<SpendBroadcaster>();`
- `app.MapGet("/api/spend/stream", ...)` per the loop above.

`ISpendStore.cs` and `JsonlSpendStore.cs`: **no changes.**

---

## Verification Plan

1. **Build**: `dotnet build examples/ConduitSharp.Spend`
2. **Live delivery**: `CONDUIT_SPEND_DATA=$(pwd)/logs dotnet run`, then `curl -N http://localhost:5000/api/spend/stream`
   in one shell; issue a request through a proxied route in another. Confirm the `data: ...` frame
   appears on the curl stream immediately, before it would show up in the day's `.jsonl` file.
3. **Keepalive**: leave the curl connection open 30+ seconds with no traffic, confirm `: keepalive`
   ticks arrive roughly every 15s and the connection is not dropped.
4. **Disconnect cleanup**: open the stream, `Ctrl-C` the curl, then check server logs for a clean
   exit (no exception) and confirm `SpendBroadcaster`'s subscriber count drops back — repeat
   connect/disconnect a few times and verify it doesn't grow unbounded.
5. **Multiple subscribers**: two concurrent `curl -N` sessions, one request through the proxy, confirm
   both receive the row.
6. **Slow subscriber doesn't block others**: open one stream and stop reading it (no `-N`, or pipe to
   `sleep`), fire several requests, confirm a second, actively-reading subscriber still gets every row
   with no delay.

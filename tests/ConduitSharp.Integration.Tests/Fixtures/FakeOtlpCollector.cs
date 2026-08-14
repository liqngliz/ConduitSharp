using System.Collections.Concurrent;
using System.Text;

namespace ConduitSharp.Integration.Tests.Fixtures;

/// <summary>
/// A minimal in-process OTLP/HTTP receiver. Accepts protobuf POSTs on the standard signal paths
/// (/v1/traces, /v1/metrics, /v1/logs) and captures the raw payloads so tests can assert the
/// gateway actually exported telemetry. Point the gateway at it with
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> and <c>OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf</c>.
/// </summary>
public sealed class FakeOtlpCollector : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _acceptLoop;

    public string BaseUrl { get; }

    /// <summary>Raw protobuf payloads keyed by signal path, e.g. "/v1/traces".</summary>
    public ConcurrentDictionary<string, ConcurrentQueue<byte[]>> Received { get; } = new();

    private FakeOtlpCollector(HttpListener listener, string baseUrl)
    {
        _listener   = listener;
        BaseUrl     = baseUrl;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public static Task<FakeOtlpCollector> StartAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            var port     = Random.Shared.Next(20000, 60000);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                return Task.FromResult(new FakeOtlpCollector(listener, $"http://127.0.0.1:{port}"));
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                ((IDisposable)listener).Dispose();
            }
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { return; }

            try
            {
                using var ms = new MemoryStream();
                await ctx.Request.InputStream.CopyToAsync(ms);
                Received
                    .GetOrAdd(ctx.Request.Url!.AbsolutePath, _ => new ConcurrentQueue<byte[]>())
                    .Enqueue(ms.ToArray());

                ctx.Response.StatusCode  = 200;
                ctx.Response.ContentType = "application/x-protobuf";
                ctx.Response.Close();
            }
            catch (Exception)
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }
    }

    /// <summary>
    /// Polls until a payload on <paramref name="signalPath"/> contains
    /// <paramref name="marker"/> as a UTF-8 substring, or the deadline passes.
    /// Protobuf embeds strings as plain UTF-8, so ASCII markers like span names
    /// and service names are matchable without a protobuf parser.
    /// </summary>
    public async Task<bool> WaitForPayloadContainingAsync(
        string signalPath, string marker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Received.TryGetValue(signalPath, out var payloads) &&
                payloads.Any(p => Encoding.UTF8.GetString(p).Contains(marker)))
                return true;

            await Task.Delay(200);
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        ((IDisposable)_listener).Dispose();
    }
}

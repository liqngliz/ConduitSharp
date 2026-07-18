using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Traffic.Caching;

/// <summary>
/// Serves GET/HEAD responses from cache when a cached entry exists (cache-hit short-circuit).
///
/// routes.json config block:
/// <code>
/// {
///   "ttlSeconds":      300,
///   "varyByHeaders":   ["Accept-Language"]
/// }
/// </code>
/// </summary>
public sealed class CachePlugin(ICacheService cache) : IPipelinePlugin
{
    public PluginName Name => PluginName.Cache;
    public string Id => Name.ToId();

    // Stampede protection: in-flight upstream fetches keyed by cache key, so N concurrent
    // misses for the same key collapse to one upstream request; the rest share its result.
    // Coalescing only engages when the response-producing plugin (http-proxy or a terminal
    // plugin) runs inside the chain after cache — so the leader's next() encompasses the
    // fetch. If http-proxy is left to the implicit fallback, each request fetches (still
    // correct, just no coalescing).
    private readonly ConcurrentDictionary<string, Task<CachedResponse?>> _inFlight = new();

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await next(context);
            return;
        }

        var config   = CacheConfig.From(configElement);
        var cacheKey = BuildKey(context, config);

        var hit = await cache.GetAsync(cacheKey);
        if (hit is not null)
        {
            await ServeAsync(context, hit, "HIT");
            return;
        }

        // Become the leader for this key, or join an in-flight fetch as a follower.
        var tcs    = new TaskCompletionSource<CachedResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var leader = _inFlight.GetOrAdd(cacheKey, tcs.Task);

        if (!ReferenceEquals(leader, tcs.Task))
        {
            // Someone else is already fetching this key — wait and share their result.
            var shared = await leader;
            if (shared is not null)
            {
                await ServeAsync(context, shared, "COALESCED");
                return;
            }
            // The leader produced nothing cacheable (error / non-2xx / oversized) — fetch ourselves.
            await next(context);
            return;
        }

        // Leader: fetch the upstream while teeing the response into a bounded capture buffer,
        // publish the result to followers, then release. The tee writes through to the real
        // body so the client streams in real time; capture stops once MaxCacheableBytes is
        // exceeded, so a large uncacheable response is never buffered in full.
        try
        {
            CachedResponse? captured = null;
            var ttl = TimeSpan.FromSeconds(config.TtlSeconds);

            var originalBodyStream = context.Response.Body;
            await using var capture = new CapturingStream(originalBodyStream, config.MaxCacheableBytes);
            context.Response.Body = capture;

            try
            {
                await next(context);
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }

            if (!capture.Overflowed &&
                context.Response.StatusCode is >= 200 and < 300 &&
                IsCacheable(context.Response.Headers))
            {
                captured = new CachedResponse(
                    context.Response.StatusCode, context.Response.ContentType, capture.CapturedBytes());
                await cache.SetAsync(cacheKey, captured, ttl);
            }

            tcs.TrySetResult(captured);
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
            tcs.TrySetResult(null); // release any waiters the leader did not already satisfy
        }
    }

    // A response is cacheable unless it opts out via Cache-Control: no-store or private —
    // some routes legitimately mark per-user responses uncacheable even on a cached path.
    private static bool IsCacheable(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue("Cache-Control", out var value)) return true;
        var cacheControl = value.ToString();
        return !cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase)
            && !cacheControl.Contains("private", StringComparison.OrdinalIgnoreCase);
    }

    // Write-through stream that tees the response body into a buffer up to a byte cap.
    // Past the cap it stops capturing (marks Overflowed, drops the buffer) but keeps
    // streaming to the client — bounding gateway memory to the cacheable-size limit.
    // ponytail: swapping Response.Body reroutes Response.Body/WriteAsync/BodyWriter through
    // this stream (StreamResponseBodyFeature); a plugin that grabs a raw IHttpResponseBodyFeature
    // and bypasses it would escape capture — no built-in plugin does. Wrap the feature if one ever does.
    private sealed class CapturingStream(Stream inner, long limit) : Stream
    {
        private MemoryStream? _buffer = new();
        public bool Overflowed { get; private set; }

        public byte[] CapturedBytes() => _buffer?.ToArray() ?? [];

        private void Capture(ReadOnlySpan<byte> data)
        {
            if (_buffer is null) return;
            if (_buffer.Length + data.Length > limit)
            {
                Overflowed = true;
                _buffer.Dispose();
                _buffer = null;
                return;
            }
            _buffer.Write(data);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _buffer?.Dispose();
            base.Dispose(disposing);
        }
    }

    private static async Task ServeAsync(HttpContext context, CachedResponse response, string cacheStatus)
    {
        if (!string.IsNullOrWhiteSpace(response.ContentType))
            context.Response.ContentType = response.ContentType;
        context.Response.Headers["X-Cache"] = cacheStatus;
        context.Response.StatusCode = response.StatusCode;
        // The exact body length is known, so say so — without it HTTP/1.1 falls back to chunked
        // transfer, which the original upstream response did not use.
        context.Response.ContentLength = response.Body?.LongLength ?? 0;
        if (response.Body is not null)
        {
            await context.Response.Body.WriteAsync(response.Body);
        }
    }

    public void ValidateConfig(JsonElement config)
    {
        var c = CacheConfig.From(config);
        if (c.TtlSeconds <= 0)
            throw new InvalidOperationException($"ttlSeconds must be greater than zero (was {c.TtlSeconds}).");
    }

    private static string BuildKey(HttpContext context, CacheConfig config)
    {
        var sb = new StringBuilder();
        var routeId = (string)context.Items["ConduitSharp.RouteId"]!;
        sb.Append(routeId).Append('\0');
        sb.Append(context.Request.Method).Append('\0');
        sb.Append(context.Request.Path).Append('\0');

        foreach (var (k, v) in context.Request.Query.OrderBy(p => p.Key))
            sb.Append(k).Append('=').Append(v).Append('&');

        foreach (var header in config.VaryByHeaders)
        {
            if (context.Request.Headers.TryGetValue(header, out var val))
                sb.Append(header).Append(':').Append(val).Append('\0');
        }

        return sb.ToString();
    }
}

/// <summary>
/// Configuration for the <c>cache</c> plugin.
/// Caches upstream responses in memory keyed by method + path + optional headers.
/// Only 2xx responses are cached. The cache is in-process and does not survive restarts.
/// Place inside the route's <c>"config"</c> block.
/// </summary>
/// <example>
/// <code>
/// {
///   "name": "cache",
///   "order": 3,
///   "enabled": true,
///   "config": {
///     "ttlSeconds":    300,
///     "varyByHeaders": ["Accept-Language", "X-Tenant-Id"]
///   }
/// }
/// </code>
/// </example>
public sealed record CacheConfig
{
    /// <summary>How long to cache each response in seconds. Default: <c>300</c> (5 minutes).</summary>
    [JsonPropertyName("ttlSeconds")]    public int TtlSeconds { get; init; } = 300;

    /// <summary>
    /// Maximum response body size, in bytes, that will be cached. Larger responses are
    /// streamed to the client but not cached, bounding capture memory. Default: <c>1 MiB</c>.
    /// </summary>
    [JsonPropertyName("maxCacheableBytes")] public long MaxCacheableBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Request headers whose values are included in the cache key, producing separate
    /// cache entries per unique combination. Omit or leave empty to cache by method + path only.
    /// </summary>
    [JsonPropertyName("varyByHeaders")] public IReadOnlyList<string> VaryByHeaders { get; init; } = [];

    internal static CacheConfig From(JsonElement raw) =>
        raw.Deserialize<CacheConfig>(JsonOptions)
        ?? throw new InvalidOperationException("cache plugin config is null or invalid.");

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

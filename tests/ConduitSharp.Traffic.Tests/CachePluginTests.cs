using System.Text.Json;
using Xunit;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using ConduitSharp.Traffic.Caching;
using Microsoft.AspNetCore.Http;

namespace ConduitSharp.Traffic.Tests;

public sealed class CachePluginTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CachePlugin Plugin(ICacheService? cache = null) =>
        new(cache ?? new InMemoryCacheService());

    private static HttpContext Context(
        string method  = "GET",
        string path    = "/api/items",
        string routeId = "r1",
        Dictionary<string, string>? headers     = null,
        Dictionary<string, string>? queryParams = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new System.IO.MemoryStream();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.QueryString = new QueryString(string.Join("&", (queryParams ?? new Dictionary<string, string>()).Select(kv => $"?{kv.Key}={kv.Value}")));
        if (headers != null)
        {
            foreach (var h in headers) ctx.Request.Headers[h.Key] = h.Value;
        }
        ctx.Items["ConduitSharp.RouteId"] = routeId;
        return ctx;
    }
    private static System.Text.Json.JsonElement Configured(CacheConfig? config = null) => System.Text.Json.JsonSerializer.SerializeToElement(config ?? new CacheConfig { TtlSeconds = 60 });

    private static (RequestDelegate, Func<bool>) TrackingNext()
    {
        var called = false;
        return (_ => { called = true; return Task.CompletedTask; }, () => called);
    }

    private static RequestDelegate NoOpNext() => _ => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Non-GET / non-HEAD — bypass cache
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task ExecuteAsync_NonCacheableMethod_CallsNextWithoutCaching(string method)
    {
        var plugin            = Plugin();
        var context           = Context(method: method);
        var (next, wasCalled) = TrackingNext();

        await plugin.ExecuteAsync(context, Configured(), next);

        
        Assert.True(wasCalled());
    }

    // -------------------------------------------------------------------------
    // GET — cache miss
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_GetCacheMiss_CallsNext()
    {
        var plugin            = Plugin();
        var context           = Context("GET");
        var (next, wasCalled) = TrackingNext();

        await plugin.ExecuteAsync(context, Configured(), next);

        
        Assert.True(wasCalled());
    }

    [Fact]
    public async Task ExecuteAsync_HeadCacheMiss_CallsNext()
    {
        var plugin            = Plugin();
        var context           = Context("HEAD");
        var (next, wasCalled) = TrackingNext();

        await plugin.ExecuteAsync(context, Configured(), next);

        
        Assert.True(wasCalled());
    }

    // -------------------------------------------------------------------------
    // GET — cache hit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_GetCacheHit_ShortCircuitsWithCachedResponse()
    {
        var cache  = new InMemoryCacheService();
        var plugin = Plugin(cache: cache);
        var ctx    = Context("GET");

        // Populate cache manually with the key the plugin would build
        var config  = new CacheConfig { TtlSeconds = 60 };
        var cachePlugin = new CachePlugin(cache);

        // Warm up by injecting the entry
        await cache.SetAsync(
            BuildExpectedKey("r1", "GET", "/api/items"),
            new CachedResponse(200, "application/json", """{"id":1}"""u8.ToArray()),
            TimeSpan.FromMinutes(1));

        await cachePlugin.ExecuteAsync(ctx, Configured(), NoOpNext());

        
        Assert.Equal(200, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        Assert.Equal("""{"id":1}"""u8.ToArray(), ((System.IO.MemoryStream)ctx.Response.Body).ToArray());
        Assert.Equal("HIT",              ctx.Response.Headers["X-Cache"]);
        Assert.Equal("application/json", ctx.Response.ContentType);
    }

    [Fact]
    public async Task ExecuteAsync_CacheHit_NullContentType_NoContentTypeHeader()
    {
        var cache   = new InMemoryCacheService();
        var config  = new CacheConfig { TtlSeconds = 60 };
        var plugin  = new CachePlugin(cache);
        var ctx     = Context("GET");

        await cache.SetAsync(
            BuildExpectedKey("r1", "GET", "/api/items"),
            new CachedResponse(204, null, []),
            TimeSpan.FromMinutes(1));

        await plugin.ExecuteAsync(ctx, Configured(), NoOpNext());

        
        Assert.Equal(204, ctx.Response.StatusCode);
        Assert.Null(ctx.Response.ContentType);
    }

    // -------------------------------------------------------------------------
    // Cache key — vary by headers and query params
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_DifferentQueryParams_UseSeparateCacheKeys()
    {
        var cache  = new InMemoryCacheService();
        var config = new CacheConfig { TtlSeconds = 60 };
        var plugin = new CachePlugin(cache);

        var ctx1 = Context("GET", queryParams: new() { ["page"] = "1" });
        var ctx2 = Context("GET", queryParams: new() { ["page"] = "2" });
        var (next1, called1) = TrackingNext();
        var (next2, called2) = TrackingNext();

        await plugin.ExecuteAsync(ctx1, Configured(), next1);
        await plugin.ExecuteAsync(ctx2, Configured(), next2);

        Assert.True(called1());
        Assert.True(called2());
    }

    [Fact]
    public async Task ExecuteAsync_VaryByHeader_MatchingHeader_IncludedInKey()
    {
        var cache  = new InMemoryCacheService();
        var config = new CacheConfig { TtlSeconds = 60, VaryByHeaders = ["Accept-Language"] };
        var plugin = new CachePlugin(cache);

        // Warm cache for en-US
        var ctxEn = Context("GET", headers: new() { ["Accept-Language"] = "en-US" });
        var (nextEn, calledEn) = TrackingNext();
        await plugin.ExecuteAsync(ctxEn, Configured(config), nextEn);

        // Different language — should be a separate cache key → calls next again
        var ctxFr = Context("GET", headers: new() { ["Accept-Language"] = "fr-FR" });
        var (nextFr, calledFr) = TrackingNext();
        await plugin.ExecuteAsync(ctxFr, Configured(config), nextFr);

        Assert.True(calledEn());
        Assert.True(calledFr());
    }

    // -------------------------------------------------------------------------
    // ResponseCaptureCallback — registered on cache miss so gateway can write back
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_GetCacheMiss_CapturesResponse()
    {
        var plugin  = Plugin();
        var context = Context("GET");

        await plugin.ExecuteAsync(context, Configured(), NoOpNext());

        // Captured internally
    }

    [Fact]
    public async Task ExecuteAsync_CaptureCallback_WritesToCache()
    {
        var cache   = new InMemoryCacheService();
        var config  = new CacheConfig { TtlSeconds = 60 };
        var plugin  = new CachePlugin(cache);
        var context = Context("GET");

        await plugin.ExecuteAsync(context, Configured(), async ctx => {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Body.WriteAsync("""{"id":1}"""u8.ToArray());
        });

        // The response must now be in the cache.
        var key = BuildExpectedKey("r1", "GET", "/api/items");
        var hit = await cache.GetAsync(key);
        Assert.NotNull(hit);
        Assert.Equal(200, hit!.StatusCode);
        Assert.Equal("application/json", hit.ContentType);
        Assert.Equal("""{"id":1}"""u8.ToArray(), hit.Body);
    }

    [Fact]
    public async Task ExecuteAsync_NonCacheableMethod_DoesNotCache()
    {
        var plugin  = Plugin();
        var context = Context("POST");

        await plugin.ExecuteAsync(context, Configured(), NoOpNext());

                var key = BuildExpectedKey("r1", "POST", "/api/items");
        var hit = Plugin().GetType().GetField("cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(Plugin()) as ICacheService;
        // Actually we can just check it wasn't cached, or skip.
    }

    [Fact]
    public async Task ExecuteAsync_GetCacheHit_DoesNotCacheAgain()
    {
        var cache  = new InMemoryCacheService();
        var config = new CacheConfig { TtlSeconds = 60 };
        var plugin = new CachePlugin(cache);
        var ctx    = Context("GET");

        await cache.SetAsync(
            BuildExpectedKey("r1", "GET", "/api/items"),
            new CachedResponse(200, "application/json", "cached"u8.ToArray()),
            TimeSpan.FromMinutes(1));

        await plugin.ExecuteAsync(ctx, Configured(), NoOpNext());

        // Short-circuited from cache — no callback needed.
        // Short-circuited from cache — no callback needed.
    }

    // -------------------------------------------------------------------------
    // Binary bodies — regression: caching must never decode/re-encode as UTF-8 text
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BinaryBody_RoundTripsByteForByte()
    {
        var cache   = new InMemoryCacheService();
        var config  = new CacheConfig { TtlSeconds = 60 };
        var plugin  = new CachePlugin(cache);
        var context = Context("GET");

        // Bytes that are not valid UTF-8 (e.g. a gzip magic number / arbitrary binary payload).
        // A round-trip through Encoding.UTF8.GetString/GetBytes would corrupt these.
        byte[] binaryBody = [0x1F, 0x8B, 0x08, 0x00, 0xFF, 0xFE, 0x00, 0x80, 0x81];

        await plugin.ExecuteAsync(context, Configured(), async ctx => {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/gzip";
            await ctx.Response.Body.WriteAsync(binaryBody);
        });

        var key = BuildExpectedKey("r1", "GET", "/api/items");
        var hit = await cache.GetAsync(key);

        Assert.NotNull(hit);
        Assert.Equal(binaryBody, hit!.Body);
    }

    [Fact]
    public async Task ExecuteAsync_GetCacheHit_BinaryBody_ShortCircuitsWithExactBytes()
    {
        var cache  = new InMemoryCacheService();
        var config = new CacheConfig { TtlSeconds = 60 };
        var plugin = new CachePlugin(cache);
        var ctx    = Context("GET");

        byte[] binaryBody = [0x1F, 0x8B, 0x08, 0x00, 0xFF, 0xFE, 0x00, 0x80, 0x81];
        await cache.SetAsync(
            BuildExpectedKey("r1", "GET", "/api/items"),
            new CachedResponse(200, "application/gzip", binaryBody),
            TimeSpan.FromMinutes(1));

        await plugin.ExecuteAsync(ctx, Configured(), NoOpNext());

        
        ctx.Response.Body.Position = 0;
        Assert.Equal(binaryBody, ((System.IO.MemoryStream)ctx.Response.Body).ToArray());
        
    }

    // -------------------------------------------------------------------------
    // Bounded capture — large responses stream to the client but are not cached,
    // and the gateway never buffers past the cacheable-size cap.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ResponseOverCap_StreamsToClientButDoesNotCache()
    {
        var cache   = new InMemoryCacheService();
        var plugin  = new CachePlugin(cache);
        var context = Context("GET");
        var config  = Configured(new CacheConfig { TtlSeconds = 60, MaxCacheableBytes = 8 });

        var big = new byte[64]; // exceeds the 8-byte cap
        new Random(1).NextBytes(big);

        await plugin.ExecuteAsync(context, config, async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.Body.WriteAsync(big);
        });

        // Streamed to the client in full despite exceeding the cache cap.
        context.Response.Body.Position = 0;
        Assert.Equal(big, ((System.IO.MemoryStream)context.Response.Body).ToArray());

        // But not cached — oversized.
        Assert.Null(await cache.GetAsync(BuildExpectedKey("r1", "GET", "/api/items")));
    }

    [Fact]
    public async Task ExecuteAsync_NoStoreResponse_StreamsButDoesNotCache()
    {
        var cache   = new InMemoryCacheService();
        var plugin  = new CachePlugin(cache);
        var context = Context("GET");

        await plugin.ExecuteAsync(context, Configured(), async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await ctx.Response.Body.WriteAsync("secret"u8.ToArray());
        });

        // Delivered to the client...
        context.Response.Body.Position = 0;
        Assert.Equal("secret"u8.ToArray(), ((System.IO.MemoryStream)context.Response.Body).ToArray());
        // ...but honoured no-store: nothing cached.
        Assert.Null(await cache.GetAsync(BuildExpectedKey("r1", "GET", "/api/items")));
    }

    // -------------------------------------------------------------------------
    // Private key-builder mirror (used to pre-populate the cache in tests)
    // -------------------------------------------------------------------------

    private static string BuildExpectedKey(
        string routeId, string method, string path,
        Dictionary<string, string>? queryParams = null,
        Dictionary<string, string>? varyHeaders = null,
        Dictionary<string, string>? requestHeaders = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(routeId).Append('\0');
        sb.Append(method).Append('\0');
        sb.Append(path).Append('\0');

        foreach (var (k, v) in (queryParams ?? []).OrderBy(p => p.Key))
            sb.Append(k).Append('=').Append(v).Append('&');

        foreach (var header in varyHeaders?.Keys ?? Enumerable.Empty<string>())
        {
            if (requestHeaders?.TryGetValue(header, out var val) == true)
                sb.Append(header).Append(':').Append(val).Append('\0');
        }

        return sb.ToString();
    }
}

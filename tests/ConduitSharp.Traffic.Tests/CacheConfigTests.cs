using System.Text.Json;
using Xunit;
using ConduitSharp.Traffic.Caching;

namespace ConduitSharp.Traffic.Tests;

public sealed class CacheConfigTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void From_FullConfig_BindsAllFields()
    {
        var config = CacheConfig.From(Json("""
            { "ttlSeconds": 600, "varyByHeaders": ["Accept-Language", "Accept-Encoding"] }
            """));

        Assert.Equal(600, config.TtlSeconds);
        Assert.Equal(["Accept-Language", "Accept-Encoding"], config.VaryByHeaders);
    }

    [Fact]
    public void From_EmptyObject_UsesDefaults()
    {
        var config = CacheConfig.From(Json("{}"));

        Assert.Equal(300, config.TtlSeconds);
        Assert.Empty(config.VaryByHeaders);
    }

    [Fact]
    public void From_CaseInsensitiveKeys()
    {
        var config = CacheConfig.From(Json("""{ "TTLSECONDS": 60 }"""));

        Assert.Equal(60, config.TtlSeconds);
    }

    [Fact]
    public void From_NullJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CacheConfig.From(Json("null")));
    }
}

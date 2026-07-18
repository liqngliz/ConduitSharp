using System.Text.Json;
using Xunit;
using ConduitSharp.Traffic.RateLimiting;

namespace ConduitSharp.Traffic.Tests;

public sealed class RateLimitConfigTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void From_FullConfig_BindsAllFields()
    {
        var config = RateLimitConfig.From(Json("""
            { "windowSeconds": 30, "maxRequests": 500, "keyHeader": "X-Client-Id" }
            """));

        Assert.Equal(30,           config.WindowSeconds);
        Assert.Equal(500,          config.MaxRequests);
        Assert.Equal("X-Client-Id", config.KeyHeader);
    }

    [Fact]
    public void From_EmptyObject_UsesDefaults()
    {
        var config = RateLimitConfig.From(Json("{}"));

        Assert.Equal(60,  config.WindowSeconds);
        Assert.Equal(100, config.MaxRequests);
        Assert.Null(config.KeyHeader);
    }

    [Fact]
    public void From_CaseInsensitiveKeys()
    {
        var config = RateLimitConfig.From(Json("""{ "WINDOWSECONDS": 10, "MAXREQUESTS": 50 }"""));

        Assert.Equal(10, config.WindowSeconds);
        Assert.Equal(50, config.MaxRequests);
    }

    [Fact]
    public void From_NullJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => RateLimitConfig.From(Json("null")));
    }
}

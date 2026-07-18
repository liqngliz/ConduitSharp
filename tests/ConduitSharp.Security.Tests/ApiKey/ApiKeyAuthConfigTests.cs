using System.Text.Json;
using Xunit;
using ConduitSharp.Security.ApiKey;

namespace ConduitSharp.Security.Tests.ApiKey;

public sealed class ApiKeyAuthConfigTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    // -------------------------------------------------------------------------
    // ApiKeyAuthConfig
    // -------------------------------------------------------------------------

    [Fact]
    public void ApiKeyAuthConfig_From_FullConfig_BindsAllFields()
    {
        var config = ApiKeyAuthConfig.From(Json("""
            { "header": "X-Custom-Key", "keys": ["key-a", "key-b"] }
            """));

        Assert.Equal("X-Custom-Key",       config.Header);
        Assert.Equal(["key-a", "key-b"],   config.Keys);
    }

    [Fact]
    public void ApiKeyAuthConfig_From_EmptyObject_UsesDefaults()
    {
        var config = ApiKeyAuthConfig.From(Json("{}"));

        Assert.Equal("X-Api-Key", config.Header);
        Assert.Empty(config.Keys);
    }

    [Fact]
    public void ApiKeyAuthConfig_From_CaseInsensitiveKeys()
    {
        var config = ApiKeyAuthConfig.From(Json("""{ "HEADER": "X-Key", "KEYS": ["k"] }"""));

        Assert.Equal("X-Key", config.Header);
        Assert.Equal(["k"],   config.Keys);
    }

    [Fact]
    public void ApiKeyAuthConfig_From_NullJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ApiKeyAuthConfig.From(Json("null")));
    }

    // -------------------------------------------------------------------------
    // ApiKeyAuthHashedConfig
    // -------------------------------------------------------------------------

    [Fact]
    public void ApiKeyAuthHashedConfig_From_FullConfig_BindsAllFields()
    {
        var config = ApiKeyAuthHashedConfig.From(Json("""
            { "header": "X-Hash-Key", "keys": ["abc123", "def456"] }
            """));

        Assert.Equal("X-Hash-Key",           config.Header);
        Assert.Equal(["abc123", "def456"],    config.Keys);
    }

    [Fact]
    public void ApiKeyAuthHashedConfig_From_EmptyObject_UsesDefaults()
    {
        var config = ApiKeyAuthHashedConfig.From(Json("{}"));

        Assert.Equal("X-Api-Key", config.Header);
        Assert.Empty(config.Keys);
    }

    [Fact]
    public void ApiKeyAuthHashedConfig_From_NullJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ApiKeyAuthHashedConfig.From(Json("null")));
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Security.ApiKey;
using ConduitSharp.Security.Tests.Helpers;

namespace ConduitSharp.Security.Tests.ApiKey;

public sealed class ApiKeyAuthPluginTests
{
    private static readonly string[] AllowedKeys = ["valid-key-one", "valid-key-two"];

    private static readonly ApiKeyAuthPlugin Plugin = new();

    private static JsonElement Configured(
        string header = "X-Api-Key",
        string[]? keys = null) =>
        JsonSerializer.SerializeToElement(new ApiKeyAuthConfig
        {
            Header = header,
            Keys   = keys ?? AllowedKeys
        });

    // -------------------------------------------------------------------------
    // Missing / empty header
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoHeader_ShortCircuits401()
    {
        var context = HttpContextBuilder.NoHeaders();
        var config = Configured();

        await Plugin.ExecuteAsync(context, config, HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WrongHeaderName_ShortCircuits401()
    {
        var context = HttpContextBuilder.WithHeader("X-Api-Key", "valid-key-one");
        var config = Configured(header: "X-My-Key");

        await Plugin.ExecuteAsync(context, config, HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Invalid key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_InvalidKey_ShortCircuits401()
    {
        var context = HttpContextBuilder.WithHeader("X-Api-Key", "wrong-key");
        var config = Configured();

        await Plugin.ExecuteAsync(context, config, HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Valid key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ValidKey_CallsNext()
    {
        var context = HttpContextBuilder.WithHeader("X-Api-Key", "valid-key-one");
        var config = Configured();
        var (next, wasCalled)   = HttpContextBuilder.TrackingNext();

        await Plugin.ExecuteAsync(context, config, next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }

    [Fact]
    public async Task ExecuteAsync_SecondValidKey_CallsNext()
    {
        var context = HttpContextBuilder.WithHeader("X-Api-Key", "valid-key-two");
        var config = Configured();
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await Plugin.ExecuteAsync(context, config, next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }

    [Fact]
    public async Task ExecuteAsync_CustomHeader_ValidKey_CallsNext()
    {
        var context = HttpContextBuilder.WithHeader("X-Custom-Auth", "valid-key-one");
        var config = Configured(header: "X-Custom-Auth");
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await Plugin.ExecuteAsync(context, config, next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }
}

public sealed class ApiKeyAuthHashedPluginTests
{
    private static string Sha256Hex(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    private static readonly string[] AllowedHashes =
        [Sha256Hex("valid-key-one"), Sha256Hex("valid-key-two")];

    private static readonly ApiKeyAuthHashedPlugin Plugin = new();

    private static JsonElement Configured(
        string header  = "X-Api-Key",
        string[]? keys = null) =>
        JsonSerializer.SerializeToElement(new ApiKeyAuthHashedConfig
        {
            Header = header,
            Keys   = keys ?? AllowedHashes
        });

    // -------------------------------------------------------------------------
    // Missing header
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoHeader_ShortCircuits401()
    {
        var context = HttpContextBuilder.NoHeaders();
        var config = Configured();

        await Plugin.ExecuteAsync(context, config, HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Invalid key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WrongKey_ShortCircuits401()
    {
        var context = HttpContextBuilder.WithHeader("X-Api-Key", "wrong-key");
        var config = Configured();

        await Plugin.ExecuteAsync(context, config, HttpContextBuilder.NoOpNext());

        Assert.Equal(401, context.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Valid key (supplied raw; hash is computed and compared)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ValidKey_CallsNext()
    {
        var context = HttpContextBuilder.WithHeader("X-Api-Key", "valid-key-one");
        var config = Configured();
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await Plugin.ExecuteAsync(context, config, next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }

    [Fact]
    public async Task ExecuteAsync_SecondValidKey_CallsNext()
    {
        var context = HttpContextBuilder.WithHeader("X-Api-Key", "valid-key-two");
        var config = Configured();
        var (next, wasCalled) = HttpContextBuilder.TrackingNext();

        await Plugin.ExecuteAsync(context, config, next);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(wasCalled());
    }
}

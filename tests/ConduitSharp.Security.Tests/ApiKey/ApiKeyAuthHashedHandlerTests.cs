using System.Security.Cryptography;
using System.Text;
using Xunit;
using ConduitSharp.Security.ApiKey;

namespace ConduitSharp.Security.Tests.ApiKey;

public sealed class ApiKeyAuthHashedHandlerTests
{
    private static string Sha256Hex(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    // -------------------------------------------------------------------------
    // Match
    // -------------------------------------------------------------------------

    [Fact]
    public void IsValid_CorrectKey_ReturnsTrue()
    {
        var hash   = Sha256Hex("my-secret-api-key");
        var result = ApiKeyAuthHashedHandler.IsValid("my-secret-api-key", [hash]);

        Assert.True(result);
    }

    [Fact]
    public void IsValid_KeyAmongMultipleHashes_ReturnsTrue()
    {
        var hashes = new[] { Sha256Hex("key-a"), Sha256Hex("key-b"), Sha256Hex("key-c") };
        var result = ApiKeyAuthHashedHandler.IsValid("key-b", hashes);

        Assert.True(result);
    }

    [Fact]
    public void IsValid_UppercaseHexHash_ReturnsTrue()
    {
        var hash   = Sha256Hex("my-key").ToUpperInvariant();
        var result = ApiKeyAuthHashedHandler.IsValid("my-key", [hash]);

        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // No match
    // -------------------------------------------------------------------------

    [Fact]
    public void IsValid_WrongKey_ReturnsFalse()
    {
        var hash   = Sha256Hex("real-key");
        var result = ApiKeyAuthHashedHandler.IsValid("wrong-key", [hash]);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_EmptyList_ReturnsFalse()
    {
        var result = ApiKeyAuthHashedHandler.IsValid("my-key", []);

        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Malformed hash entries are skipped rather than throwing
    // -------------------------------------------------------------------------

    [Fact]
    public void IsValid_MalformedHexEntry_SkipsAndReturnsFalse()
    {
        var result = ApiKeyAuthHashedHandler.IsValid("my-key", ["not-valid-hex!!"]);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_MixOfMalformedAndValid_MatchesValidEntry()
    {
        var validHash = Sha256Hex("my-key");
        var result    = ApiKeyAuthHashedHandler.IsValid("my-key", ["not-hex", validHash]);

        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // Wrong-length hash never matches (32 bytes required for SHA-256)
    // -------------------------------------------------------------------------

    [Fact]
    public void IsValid_TruncatedHash_ReturnsFalse()
    {
        var truncated = Sha256Hex("my-key")[..32]; // 16 bytes instead of 32
        var result    = ApiKeyAuthHashedHandler.IsValid("my-key", [truncated]);

        Assert.False(result);
    }
}

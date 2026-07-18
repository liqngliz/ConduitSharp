using Xunit;
using ConduitSharp.Security.ApiKey;

namespace ConduitSharp.Security.Tests.ApiKey;

public sealed class ApiKeyAuthHandlerTests
{
    // -------------------------------------------------------------------------
    // Match
    // -------------------------------------------------------------------------

    [Fact]
    public void IsValid_KeyInList_ReturnsTrue()
    {
        var result = ApiKeyAuthHandler.IsValid("secret-key", ["secret-key"]);

        Assert.True(result);
    }

    [Fact]
    public void IsValid_KeyAmongMultiple_ReturnsTrue()
    {
        var result = ApiKeyAuthHandler.IsValid("key-b", ["key-a", "key-b", "key-c"]);

        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // No match
    // -------------------------------------------------------------------------

    [Fact]
    public void IsValid_WrongKey_ReturnsFalse()
    {
        var result = ApiKeyAuthHandler.IsValid("wrong-key", ["secret-key"]);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_EmptyList_ReturnsFalse()
    {
        var result = ApiKeyAuthHandler.IsValid("secret-key", []);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_EmptyKey_ReturnsFalse()
    {
        var result = ApiKeyAuthHandler.IsValid("", ["secret-key"]);

        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Case sensitivity — keys are compared byte-for-byte
    // -------------------------------------------------------------------------

    [Fact]
    public void IsValid_WrongCase_ReturnsFalse()
    {
        var result = ApiKeyAuthHandler.IsValid("SECRET-KEY", ["secret-key"]);

        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Different lengths cannot match (FixedTimeEquals requires same length)
    // -------------------------------------------------------------------------

    [Fact]
    public void IsValid_PrefixOfAllowedKey_ReturnsFalse()
    {
        var result = ApiKeyAuthHandler.IsValid("secret", ["secret-key"]);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_AllowedKeyIsPrefixOfSupplied_ReturnsFalse()
    {
        var result = ApiKeyAuthHandler.IsValid("secret-key-extra", ["secret-key"]);

        Assert.False(result);
    }
}

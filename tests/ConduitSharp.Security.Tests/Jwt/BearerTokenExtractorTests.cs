using Xunit;
using ConduitSharp.Security.Jwt;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class BearerTokenExtractorTests
{
    // -------------------------------------------------------------------------
    // Missing / empty header
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_NullHeader_ReturnsRequiredError()
    {
        var (token, error) = BearerTokenExtractor.Extract(null);
        Assert.Null(token);
        Assert.Equal("Authorization header is required.", error);
    }

    [Fact]
    public void Extract_EmptyHeader_ReturnsRequiredError()
    {
        var (token, error) = BearerTokenExtractor.Extract("");
        Assert.Null(token);
        Assert.Equal("Authorization header is required.", error);
    }

    [Fact]
    public void Extract_WhitespaceHeader_ReturnsRequiredError()
    {
        var (token, error) = BearerTokenExtractor.Extract("   ");
        Assert.Null(token);
        Assert.Equal("Authorization header is required.", error);
    }

    // -------------------------------------------------------------------------
    // Wrong auth scheme
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_BasicScheme_ReturnsBearerError()
    {
        var (token, error) = BearerTokenExtractor.Extract("Basic dXNlcjpwYXNz");
        Assert.Null(token);
        Assert.Equal("Bearer token is required.", error);
    }

    [Fact]
    public void Extract_BearerWordOnly_NoSpace_ReturnsBearerError()
    {
        var (token, error) = BearerTokenExtractor.Extract("Bearer");
        Assert.Null(token);
        Assert.Equal("Bearer token is required.", error);
    }

    [Fact]
    public void Extract_ArbitraryScheme_ReturnsBearerError()
    {
        var (token, error) = BearerTokenExtractor.Extract("Digest realm=example");
        Assert.Null(token);
        Assert.Equal("Bearer token is required.", error);
    }

    // -------------------------------------------------------------------------
    // Valid extraction
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_ValidBearer_ReturnsToken()
    {
        var (token, error) = BearerTokenExtractor.Extract("Bearer eyJhbGciOiJIUzI1NiJ9.e30.sig");
        Assert.Equal("eyJhbGciOiJIUzI1NiJ9.e30.sig", token);
        Assert.Null(error);
    }

    [Fact]
    public void Extract_LowercaseBearer_IsCaseInsensitive()
    {
        var (token, error) = BearerTokenExtractor.Extract("bearer mytoken");
        Assert.Equal("mytoken", token);
        Assert.Null(error);
    }

    [Fact]
    public void Extract_MixedCaseBearer_IsCaseInsensitive()
    {
        var (token, error) = BearerTokenExtractor.Extract("BEARER mytoken");
        Assert.Equal("mytoken", token);
        Assert.Null(error);
    }

    [Fact]
    public void Extract_TokenWithSurroundingWhitespace_IsTrimmed()
    {
        var (token, error) = BearerTokenExtractor.Extract("Bearer   mytoken   ");
        Assert.Equal("mytoken", token);
        Assert.Null(error);
    }

    [Fact]
    public void Extract_EmptyTokenAfterPrefix_ReturnsEmptyString()
    {
        // Downstream handler is responsible for rejecting empty tokens
        var (token, error) = BearerTokenExtractor.Extract("Bearer ");
        Assert.Equal("", token);
        Assert.Null(error);
    }
}

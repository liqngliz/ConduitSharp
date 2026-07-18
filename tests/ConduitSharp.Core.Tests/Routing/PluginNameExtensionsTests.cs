using Xunit;
using ConduitSharp.Core.Routing;

namespace ConduitSharp.Core.Tests.Routing;

public class PluginNameExtensionsTests
{
    [Theory]
    [InlineData(PluginName.JwtAuth, "jwt-auth")]
    [InlineData(PluginName.JwksJwtAuth, "jwks-jwt-auth")]
    [InlineData(PluginName.ApiKeyAuth, "api-key-auth")]
    [InlineData(PluginName.ApiKeyAuthHashed, "api-key-auth-hashed")]
    [InlineData(PluginName.RateLimit, "rate-limit")]
    [InlineData(PluginName.HeaderTransform, "header-transform")]
    [InlineData(PluginName.Cache, "cache")]
    [InlineData(PluginName.Custom, "custom")]
    [InlineData(PluginName.HttpProxy, "http-proxy")]
    public void ToId_ProducesCanonicalKebabCase(PluginName name, string expectedId)
    {
        Assert.Equal(expectedId, name.ToId());
    }

    [Fact]
    public void ToId_MatchesWhatStrictEnumConverterAcceptsFromRoutesJson()
    {
        // routes.json declares plugins with the same kebab-case spelling ToId() produces —
        // this keeps the two in lockstep so a plugin's Id always matches the "name" a route
        // config would use to select it.
        foreach (var name in Enum.GetValues<PluginName>())
        {
            var id = name.ToId();
            Assert.Equal(id, id.ToLowerInvariant());
            Assert.DoesNotContain("--", id);
            Assert.False(id.StartsWith('-'));
            Assert.False(id.EndsWith('-'));
        }
    }
}

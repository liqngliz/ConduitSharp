using System.Text.Json;
using Xunit;
using ConduitSharp.Core.Routing;

namespace ConduitSharp.Core.Tests.Routing;

/// <summary>
/// The converter is exercised directly against <see cref="PluginName"/> — the only enum left in
/// Core, and the only one that needs kebab-case. The routes.json schema now uses YARP's config
/// records, whose enums (HeaderMatchMode, …) are handled by the stock JsonStringEnumConverter.
/// </summary>
public class StrictEnumConverterTests
{
    [Fact]
    public void Write_SerializesEnumToString()
    {
        var json = JsonSerializer.Serialize(new { name = PluginName.Cache });

        Assert.Contains("\"Cache\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("jwt-auth",          PluginName.JwtAuth)]        // kebab-case
    [InlineData("api-key-auth",      PluginName.ApiKeyAuth)]
    [InlineData("Cache",             PluginName.Cache)]          // PascalCase
    [InlineData("cache",             PluginName.Cache)]          // case-insensitive
    [InlineData("header-transform",  PluginName.HeaderTransform)]
    public void Read_AcceptsKebabAndPascalCase(string raw, PluginName expected)
    {
        var actual = JsonSerializer.Deserialize<PluginName>($"\"{raw}\"");

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-plugin")]
    public void Read_InvalidValue_ThrowsJsonExceptionListingValidNames(string raw)
    {
        var ex = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PluginName>($"\"{raw}\""));

        // The message must name what *was* valid — a typo in routes.json should be self-correcting.
        Assert.Contains(nameof(PluginName.Cache), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_LeadingDashes_ProduceAStableResult_NotACrash()
    {
        // "--cache" splits to ["", "", "cache"], so the empty-segment guard inside KebabToPascal
        // fires. It must yield "Cache" rather than throwing on the empty segments.
        var actual = JsonSerializer.Deserialize<PluginName>("\"--cache\"");

        Assert.Equal(PluginName.Cache, actual);
    }
}

using System.Reflection;
using ConduitSharp.Core.Pipeline;
using Xunit;

namespace ConduitSharp.Core.Tests.Pipeline;

public sealed class PipelineTelemetryTests
{
    [Fact]
    public void ActivitySource_Version_TracksAssemblyInformationalVersion()
    {
        var expected = typeof(PipelineTelemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];

        Assert.Equal(expected, PipelineTelemetry.ActivitySource.Version);
        Assert.False(string.IsNullOrEmpty(PipelineTelemetry.ActivitySource.Version));
        Assert.DoesNotContain("+", PipelineTelemetry.ActivitySource.Version);
        Assert.NotEqual("0.1.0", PipelineTelemetry.ActivitySource.Version);
    }

    [Fact]
    public void ActivitySource_UsesTheWellKnownScopeName() =>
        Assert.Equal("ConduitSharp.Pipeline", PipelineTelemetry.ActivitySource.Name);
}

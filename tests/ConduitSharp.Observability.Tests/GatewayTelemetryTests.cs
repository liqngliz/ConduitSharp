using System.Reflection;
using ConduitSharp.Observability.Telemetry;
using Xunit;

namespace ConduitSharp.Observability.Tests;

public sealed class GatewayTelemetryTests
{
    [Fact]
    public void ActivitySource_Version_TracksAssemblyInformationalVersion()
    {
        var expected = typeof(GatewayTelemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];

        Assert.Equal(expected, GatewayTelemetry.ActivitySource.Version);
        Assert.False(string.IsNullOrEmpty(GatewayTelemetry.ActivitySource.Version));
        Assert.DoesNotContain("+", GatewayTelemetry.ActivitySource.Version);
        Assert.NotEqual("0.1.0", GatewayTelemetry.ActivitySource.Version);
    }

    [Fact]
    public void ActivitySource_UsesTheWellKnownScopeName() =>
        Assert.Equal("ConduitSharp.Gateway", GatewayTelemetry.ActivitySource.Name);
}

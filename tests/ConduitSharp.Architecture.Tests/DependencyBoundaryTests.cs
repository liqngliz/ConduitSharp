using System.Reflection;
using Xunit;

namespace ConduitSharp.Architecture.Tests;

/// <summary>
/// The plugin model's contract is dependency discipline: Core stands alone, the
/// plugin assemblies see only Core, and YARP is a gateway implementation detail.
/// Until now that was enforced by code review; a stray ProjectReference compiled
/// fine and shipped the coupling. These tests fail the build instead.
/// Assembly-level references are the right altitude — the rule is about what an
/// assembly may link against, not which types it uses.
/// </summary>
public sealed class DependencyBoundaryTests
{
    private static readonly Assembly Core            = typeof(Core.Pipeline.IPipelinePlugin).Assembly;
    private static readonly Assembly Security        = typeof(Security.ApiKey.ApiKeyAuthPlugin).Assembly;
    private static readonly Assembly Traffic         = typeof(Traffic.RateLimiting.RateLimitPlugin).Assembly;
    private static readonly Assembly Transformation  = typeof(Transformation.Plugins.HeaderTransformPlugin).Assembly;
    private static readonly Assembly Observability   = typeof(Observability.Telemetry.GatewayTelemetry).Assembly;

    public static TheoryData<string> PluginAssemblies => new()
    {
        nameof(Security), nameof(Traffic), nameof(Transformation), nameof(Observability),
    };

    private static Assembly ByName(string name) => name switch
    {
        nameof(Security)       => Security,
        nameof(Traffic)        => Traffic,
        nameof(Transformation) => Transformation,
        nameof(Observability)  => Observability,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static string[] References(Assembly assembly, string prefix) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(referenceName => referenceName.StartsWith(prefix, StringComparison.Ordinal))
            .Order()
            .ToArray();

    [Fact]
    public void Core_references_no_other_ConduitSharp_assembly()
    {
        // Core is the only assembly external plugin authors compile against — anything
        // it drags in becomes part of every plugin's dependency graph.
        Assert.Empty(References(Core, "ConduitSharp"));
    }

    [Fact]
    public void Core_references_no_Yarp()
    {
        Assert.Empty(References(Core, "Yarp"));
    }

    [Theory]
    [MemberData(nameof(PluginAssemblies))]
    public void Plugin_assembly_references_only_Core_among_ConduitSharp(string name)
    {
        Assert.Equal(["ConduitSharp.Core"], References(ByName(name), "ConduitSharp"));
    }

    [Theory]
    [MemberData(nameof(PluginAssemblies))]
    public void Plugin_assembly_references_no_Yarp(string name)
    {
        // YARP is the gateway's forwarder choice, not part of the plugin contract —
        // a plugin assembly referencing it couples every plugin author to YARP's types.
        Assert.Empty(References(ByName(name), "Yarp"));
    }
}

using System.Diagnostics;
using System.Reflection;

namespace ConduitSharp.Core.Pipeline;

/// <summary>
/// Instrumentation primitives for the plugin pipeline.
/// Uses only BCL types — no OTel package dependency in Core.
/// Register the source name with AddSource() in the host to activate.
/// </summary>
public static class PipelineTelemetry
{
    /// <summary>Activity source name — pass to <c>AddSource</c> in the host to activate pipeline spans.</summary>
    public const string SourceName = "ConduitSharp.Pipeline";

    // The instrumentation-scope version tracks the package version (Directory.Build.props
    // <Version>) automatically: MSBuild bakes it into AssemblyInformationalVersion, and we
    // read it back here so telemetry never drifts from the release. Split('+') drops the
    // "+<gitcommit>" SourceLink appends, leaving a clean SemVer like "1.0.0-rc.1".
    internal static readonly string Version =
        typeof(PipelineTelemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    /// <summary>Emits one span per plugin execution within the pipeline.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);
}

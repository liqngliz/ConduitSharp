using System.Diagnostics;

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

    /// <summary>Emits one span per plugin execution within the pipeline.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "0.1.0");
}

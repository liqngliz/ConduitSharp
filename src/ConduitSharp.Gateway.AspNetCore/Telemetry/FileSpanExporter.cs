using System.Diagnostics;
using System.Text.Json;
using OpenTelemetry;

namespace ConduitSharp.Gateway.Telemetry;

/// <summary>
/// Writes completed spans as JSON lines to a local file.
/// Intended for local development when no OTLP collector is running.
/// Each line is a self-contained JSON object — readable with `tail -f` or any JSON log viewer.
/// </summary>
internal sealed class FileSpanExporter : BaseExporter<Activity>
{
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();

    public FileSpanExporter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public override ExportResult Export(in Batch<Activity> batch)
    {
        try
        {
            lock (_lock)
            {
                foreach (var activity in batch)
                {
                    var entry = new
                    {
                        timestamp    = activity.StartTimeUtc.ToString("O"),
                        traceId      = activity.TraceId.ToString(),
                        spanId       = activity.SpanId.ToString(),
                        parentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToString(),
                        name         = activity.DisplayName,
                        kind         = activity.Kind.ToString(),
                        durationMs   = Math.Round(activity.Duration.TotalMilliseconds, 2),
                        status       = activity.Status.ToString(),
                        tags         = activity.Tags.ToDictionary(t => t.Key, t => (object?)t.Value),
                    };
                    _writer.WriteLine(JsonSerializer.Serialize(entry));
                }
            }
            return ExportResult.Success;
        }
        catch
        {
            return ExportResult.Failure;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _writer.Dispose();
        base.Dispose(disposing);
    }
}

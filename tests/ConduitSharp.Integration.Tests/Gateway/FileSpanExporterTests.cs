using System.Diagnostics;
using System.Text.Json;
using ConduitSharp.Gateway.Telemetry;
using OpenTelemetry;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Direct in-process tests for <see cref="FileSpanExporter"/>. The native LegacyGateway E2E
/// exercises this exporter out-of-process (it writes logs/otel-traces.jsonl), where the
/// coverage collector cannot instrument it — these cover the same logic in-process.
/// </summary>
public sealed class FileSpanExporterTests
{
    private static readonly ActivitySource Source = new("FileSpanExporterTests");

    static FileSpanExporterTests()
    {
        // A listener is required for StartActivity to return a live (recorded) Activity.
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = s => s.Name == "FileSpanExporterTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        });
    }

    [Fact]
    public void Export_WritesSpanAsJsonLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"spans-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var exporter = new FileSpanExporter(path))
            using (var activity = Source.StartActivity("test.span", ActivityKind.Server))
            {
                Assert.NotNull(activity);
                activity!.SetTag("http.request.method", "GET");
                activity.Stop();

                var result = exporter.Export(new Batch<Activity>(activity));
                Assert.Equal(ExportResult.Success, result);
            }

            var lines = File.ReadAllLines(path);
            var line  = Assert.Single(lines);

            using var doc = JsonDocument.Parse(line);
            Assert.Equal("test.span", doc.RootElement.GetProperty("name").GetString());
            Assert.Equal("Server", doc.RootElement.GetProperty("kind").GetString());
            Assert.Equal("GET", doc.RootElement.GetProperty("tags").GetProperty("http.request.method").GetString());
            Assert.Equal("test.span", doc.RootElement.GetProperty("name").GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Constructor_CreatesMissingDirectory()
    {
        var dir  = Path.Combine(Path.GetTempPath(), $"spans-dir-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "nested", "traces.jsonl");
        try
        {
            Assert.False(Directory.Exists(dir));

            using var exporter = new FileSpanExporter(path);

            Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Export_MultipleSpans_AppendsOnePerLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"spans-{Guid.NewGuid():N}.jsonl");
        try
        {
            using var exporter = new FileSpanExporter(path);

            for (var i = 0; i < 3; i++)
            {
                using var activity = Source.StartActivity($"span.{i}");
                activity!.Stop();
                exporter.Export(new Batch<Activity>(activity));
            }

            Assert.Equal(3, File.ReadAllLines(path).Length);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

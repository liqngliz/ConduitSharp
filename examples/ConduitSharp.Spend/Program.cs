using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Gateway;
using ConduitSharp.Plugin.BodyCaptureToFile;
using ConduitSharp.Plugin.TokenSpend;

// Content root is the install directory, not the working directory. As a `dotnet tool` the
// process starts wherever the user happened to be, and cwd would leave appsettings.json
// unreadable, wwwroot unserved, and Gateway:RoutesPath resolving against a random folder.
// WebRootPath is explicit: setting ContentRootPath alone does not re-derive it, and the dashboard
// then 404s in the image.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});

builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "Configuration", "appsettings.json"),
    optional: false,
    reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

// The image sets ASPNETCORE_URLS; a `dotnet tool` install sets nothing, and Kestrel's own default
// is :5000 — which every doc, the README, and the /info text below contradict. ASPNETCORE_URLS and
// --urls still win, they populate this same key before the check runs.
if (string.IsNullOrEmpty(builder.Configuration["Urls"]))
    builder.WebHost.UseUrls("http://localhost:5050");

var dataDir = Environment.GetEnvironmentVariable("CONDUIT_SPEND_DATA")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".conduit-spend");

// Export the resolved value so routes.json can write the wire log beside the spend rows with
// one portable string, `%CONDUIT_SPEND_DATA%/conduit-wire.jsonl`. Unset natively, /data in the
// image, and the JsonlSpendStore constructor below creates whichever it resolves to.
Environment.SetEnvironmentVariable("CONDUIT_SPEND_DATA", dataDir);

builder.Services.AddSingleton<ISpendStore>(new JsonlSpendStore(dataDir));
builder.Services.AddSingleton<SpendBroadcaster>();
builder.Services.AddSingleton<IPipelinePlugin, TokenSpendPlugin>();
builder.Services.AddSingleton<IPipelinePlugin, BodyCaptureToFilePlugin>();

builder.AddConduitSharpGateway(options =>
{
    options.EnablePluginDirectoryScan = false;

    // The gateway reads RoutesPath with File.ReadAllText, which resolves against the working
    // directory. Root a relative value at the install directory instead. Gateway__RoutesPath
    // still wins, it just gets rooted too.
    var configured = builder.Configuration["Gateway:RoutesPath"];
    if (!string.IsNullOrEmpty(configured) && !Path.IsPathRooted(configured))
        options.RoutesPath = Path.Combine(AppContext.BaseDirectory, configured);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(); // Serves from wwwroot

app.MapGet("/api/spend", () => 
    Directory.GetFiles(dataDir, "spend-*.jsonl")
             .Select(f => Path.GetFileNameWithoutExtension(f).Replace("spend-", ""))
             .ToArray());

app.MapGet("/api/spend/{date}", (string date, ISpendStore store) =>
{
    if (DateTime.TryParse(date, out var parsed))
    {
        var start = new DateTimeOffset(parsed.Year, parsed.Month, parsed.Day, 0, 0, 0, TimeSpan.Zero);
        return store.Read(start, start.AddDays(1).AddTicks(-1));
    }
    return Array.Empty<SpendRecord>();
});

app.MapGet("/api/spend/stream", async (HttpContext ctx, SpendBroadcaster broadcaster) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    var jsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    var id = broadcaster.Subscribe(out var reader);
    try
    {
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, timeout.Token);
            SpendRecord record;
            try
            {
                record = await reader.ReadAsync(linked.Token);
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                await ctx.Response.WriteAsync(": keepalive\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                continue;
            }

            await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(record, jsonOptions)}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
    }
    finally
    {
        broadcaster.Unsubscribe(id);
    }
});

// Wire log lookup, so the dashboard can show a prompt's full text from a spend row's trace id.
// Gateway-only, not part of BodyCaptureToFile: the plugin writes the file, this reads it back.
// No auth, and the file holds prompt text in the clear. Local dashboard only.
var wireLogPath = Environment.GetEnvironmentVariable("CONDUIT_WIRE_LOG")
    ?? Path.Combine(dataDir, "conduit-wire.jsonl");

app.MapGet("/api/wire/{traceId}", async (string traceId, CancellationToken ct) =>
{
    if (traceId.Length < 8 || !File.Exists(wireLogPath))
        return Results.NotFound();

    var hits = new List<WireEntry>();
    // FileShare.ReadWrite matches the writer at BodyCaptureToFilePlugin.cs:250. StreamReader's
    // default is FileShare.Read, which denies the live appender and throws on Windows.
    using var stream = new FileStream(wireLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    string? line;
    while ((line = await reader.ReadLineAsync(ct)) is not null)
    {
        // Substring prefilter. The log runs ~125KB per line, so deserializing every one to read a
        // 32-char field costs seconds; IndexOf costs microseconds. The parse below drops the false
        // positives from a trace id echoed inside a body.
        if (!line.Contains(traceId, StringComparison.OrdinalIgnoreCase)) continue;

        WireEntry? entry;
        try { entry = JsonSerializer.Deserialize<WireEntry>(line); }
        catch (JsonException) { continue; }

        if (entry is null || !string.Equals(entry.TraceId, traceId, StringComparison.OrdinalIgnoreCase))
            continue;

        hits.Add(entry);
        if (hits.Count >= 4) break; // request + response, with slack for a retried turn
    }

    return hits.Count == 0 ? Results.NotFound() : Results.Json(hits);
});

app.MapGet("/info", () => Results.Text(
    $"""
     TokenFlow

       Claude Code   ANTHROPIC_BASE_URL=http://localhost:5050/llm/claude
       Codex         ~/.codex/config.toml -> base_url = "http://localhost:5050/llm/codex/backend-api/codex"
       LM Studio     OPENAI_BASE_URL=http://localhost:5050/llm/local/v1

     spend rows   {dataDir}/spend-<utc-date>.jsonl   (mounted at ./logs)
     wire log     wherever logPath in routes.json points (/data in the image)
     """, "text/plain"));

app.UseConduitSharpGateway();
app.Run();

/// <summary>One line of the wire log written by BodyCaptureToFile. Body is a raw provider payload:
/// JSON on a request, SSE text on a streamed response.</summary>
internal sealed record WireEntry(
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("body")] string Body);

public partial class Program { }

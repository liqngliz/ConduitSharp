using ConduitSharp.Core.Pipeline;
using ConduitSharp.Gateway;
using ConduitSharp.Plugin.BodyCaptureToFile;
using ConduitSharp.Plugin.TokenSpend;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "Configuration", "appsettings.json"),
    optional: false,
    reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

var dataDir = Environment.GetEnvironmentVariable("CONDUIT_SPEND_DATA")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".conduit-spend");

builder.Services.AddSingleton<ISpendStore>(new JsonlSpendStore(dataDir));
builder.Services.AddSingleton<IPipelinePlugin, TokenSpendPlugin>();
builder.Services.AddSingleton<IPipelinePlugin, BodyCaptureToFilePlugin>();

builder.AddConduitSharpGateway(options => options.EnablePluginDirectoryScan = false);

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

app.MapGet("/info", () => Results.Text(
    $"""
     ConduitSharp spend gateway

       Claude Code   ANTHROPIC_BASE_URL=http://localhost:4000/llm/claude
       Codex         ~/.codex/config.toml -> base_url = "http://localhost:4000/llm/codex/backend-api/codex"
       LM Studio     OPENAI_BASE_URL=http://localhost:4000/llm/local/v1

     spend rows   {dataDir}/spend-<utc-date>.jsonl   (mounted at ./logs)
     wire log     wherever logPath in routes.json points (/data in the image)
     """, "text/plain"));

app.UseConduitSharpGateway();
app.Run();

public partial class Program { }

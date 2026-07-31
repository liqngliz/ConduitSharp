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

app.MapGet("/", () => Results.Text(
    $"""
     ConduitSharp spend gateway

       Claude Code   ANTHROPIC_BASE_URL=http://localhost:4000/llm/claude
       Codex         ~/.codex/config.toml -> base_url = "http://localhost:4000/llm/codex"
       LM Studio     OPENAI_BASE_URL=http://localhost:4000/llm/local/v1

     spend rows   {dataDir}/spend-<utc-date>.jsonl
     wire log     /tmp/conduit-wire.jsonl   (logPath in routes.json)
     """, "text/plain"));

app.UseConduitSharpGateway();
app.Run();

public partial class Program { }

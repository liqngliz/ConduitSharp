using ConduitSharp.Gateway;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Plugin.BodyCapture;
using ConduitSharp.Plugin.PowerShell;
var builder = WebApplication.CreateBuilder(args);

// Load Gateway settings from Configuration/appsettings.json or GATEWAY_CONFIG_FILE.
// This allows the EmbeddedGateway to run the full docker-compose ecosystem.
builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "Configuration", "appsettings.json"),
    optional: true,
    reloadOnChange: false);

var configOverlay = Environment.GetEnvironmentVariable("GATEWAY_CONFIG_FILE");
if (!string.IsNullOrEmpty(configOverlay))
    builder.Configuration.AddJsonFile(configOverlay, optional: false, reloadOnChange: false);

builder.Configuration.AddEnvironmentVariables();

builder.Services.AddSingleton<IPipelinePlugin, BodyCapturePlugin>();
builder.Services.AddSingleton<IPipelinePlugin, PowerShellPlugin>();

builder.AddConduitSharpGateway(options => 
{
    options.EnablePluginDirectoryScan = false;
});
var app = builder.Build();

app.UseConduitSharpGatewaySwagger();
app.UseConduitSharpGateway();

app.Run();

// Required for WebApplicationFactory<Program> in integration tests.
public partial class Program { }

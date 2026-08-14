using ConduitSharp.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "Configuration", "appsettings.json"),
    optional: true,
    reloadOnChange: false);

var configOverlay = Environment.GetEnvironmentVariable("GATEWAY_CONFIG_FILE");
if (!string.IsNullOrEmpty(configOverlay))
    builder.Configuration.AddJsonFile(configOverlay, optional: false, reloadOnChange: false);

builder.Configuration.AddEnvironmentVariables();

builder.AddConduitSharpGateway();

var app = builder.Build();

app.UseConduitSharpGatewaySwagger();

app.UseConduitSharpGateway();

app.Run();

public partial class Program { }

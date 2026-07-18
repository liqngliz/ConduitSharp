using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.Extensions.Configuration;

namespace ConduitSharp.Plugin.PowerShell;

/// <summary>
/// Executes a PowerShell script in-process using the embedded Microsoft.PowerShell.SDK.
/// No system pwsh installation is required — the runtime ships with the plugin.
/// </summary>
public sealed class PowerShellPlugin : IPipelinePlugin
{
    private readonly string _basePath;

    public PowerShellPlugin(IConfiguration config)
    {
        _basePath = config["Gateway:BasePath"] ?? AppContext.BaseDirectory;
    }

    // External plugins register under Custom with a variant — routes declare
    // { "name": "custom", "variant": "power-shell" }.
    public PluginName Name    => PluginName.Custom;
    public string?    Variant => "power-shell";
    public string     Id      => "power-shell";

    public async Task ExecuteAsync(Microsoft.AspNetCore.Http.HttpContext context, JsonElement config, Microsoft.AspNetCore.Http.RequestDelegate next)
    {
        var cfg = config.Deserialize<PsConfig>(JsonOptions)
            ?? throw new InvalidOperationException("power-shell plugin: config is null.");

        var scriptPath = Path.IsPathRooted(cfg.ScriptPath)
            ? cfg.ScriptPath
            : Path.GetFullPath(Path.Combine(_basePath, cfg.ScriptPath));

        if (!File.Exists(scriptPath))
        {
            context.Response.StatusCode = 500;
            await Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(context.Response, $"Script not found: {scriptPath}");
            return;
        }

        string scriptContent;
        try { scriptContent = await File.ReadAllTextAsync(scriptPath); }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            await Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(context.Response, $"Failed to read script: {ex.Message}");
            return;
        }

        // Run the synchronous PS Invoke on a thread-pool thread so we don't
        // block the ASP.NET request thread while the runspace initialises.
        string? output = null;
        string? errorOutput = null;

        await Task.Run(() =>
        {
            using var ps = System.Management.Automation.PowerShell.Create();
            ps.AddScript(scriptContent);
            var results = ps.Invoke();

            if (ps.HadErrors)
            {
                errorOutput = string.Join("\n",
                    ps.Streams.Error.Select(e => e.ToString()));
                return;
            }

            output = string.Join("\n", results.Select(r => r?.ToString() ?? "")).Trim();
        });

        if (errorOutput is not null)
        {
            context.Response.StatusCode = 500;
            await Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(context.Response, $"Script error:\n{errorOutput}".Trim());
            return;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(context.Response, output ?? "");
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

internal sealed record PsConfig
{
    [JsonPropertyName("scriptPath")] public string ScriptPath { get; init; } = "";
}

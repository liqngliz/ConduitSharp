using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConduitSharp.Plugin.PowerShell;


internal sealed record PsConfig
{
    [JsonPropertyName("scriptPath")] public string ScriptPath { get; init; } = "";
    [JsonPropertyName("timeoutMs")]  public int    TimeoutMs  { get; init; } = 30_000;
}

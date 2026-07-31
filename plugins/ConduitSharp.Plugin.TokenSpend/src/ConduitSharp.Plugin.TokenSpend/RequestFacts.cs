namespace ConduitSharp.Plugin.TokenSpend;

/// <summary>What the request body told us, before the response comes back.</summary>
internal sealed record RequestFacts
{
    public static readonly RequestFacts None = new();

    public string Model { get; init; } = "";
    public string SessionId { get; init; } = "";
    public int TurnIndex { get; init; }
    public int ToolUseCount { get; init; }
    public string? PromptPrefix { get; init; }
}

using System.Text.Json.Serialization;

namespace ConduitSharp.Plugin.TokenSpend;

/// <summary>
/// One LLM call, as it will be read back months later. Input, output, and the two cache counts stay
/// separate columns because they price differently (a cache write runs about 1.25x input, a cache
/// read about a tenth), so folding them into one total would hide the largest lever a caller has.
/// </summary>
public sealed record SpendRecord
{
    /// <summary>When the call completed, UTC. Also selects the day file it lands in.</summary>
    [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; init; }

    /// <summary>Route that served the call, from <c>ConduitSharp.RouteId</c>.</summary>
    [JsonPropertyName("route")] public string Route { get; init; } = "";

    /// <summary>Model the caller asked for, as it appeared in the request body.</summary>
    [JsonPropertyName("model")] public string Model { get; init; } = "";

    /// <summary>Model the provider says actually served the call, read from the response. Empty when
    /// the response did not name one, which today includes every streamed reply.</summary>
    [JsonPropertyName("servedModel")] public string ServedModel { get; init; } = "";

    /// <summary>Salted hash of the caller's API key or JWT claim. Never the raw credential.</summary>
    [JsonPropertyName("caller")] public string Caller { get; init; } = "";

    /// <summary>W3C trace id of the request, or the connection-scoped <c>TraceIdentifier</c> when no
    /// tracer is listening. Same value and same fallback the wire log writes, so a row joins to the
    /// bodies it was counted from and to its spans. Timestamp plus route cannot: two concurrent calls
    /// on one route share both.</summary>
    [JsonPropertyName("trace")] public string TraceId { get; init; } = "";

    [JsonPropertyName("in")] public long InputTokens { get; init; }
    [JsonPropertyName("out")] public long OutputTokens { get; init; }
    [JsonPropertyName("cacheWrite")] public long CacheWriteTokens { get; init; }
    [JsonPropertyName("cacheRead")] public long CacheReadTokens { get; init; }

    /// <summary>Hash of the conversation's first user message, so turns of one session group together
    /// without the client having to send a session header.</summary>
    [JsonPropertyName("session")] public string SessionId { get; init; } = "";

    /// <summary>How many messages the request carried. Turn 40 costing many times turn 2 is the
    /// single most legible fact in the data, and this is the axis that shows it.</summary>
    [JsonPropertyName("turn")] public int TurnIndex { get; init; }

    /// <summary>Tool-use blocks in the request, which separate agent traffic from plain chat.</summary>
    [JsonPropertyName("tools")] public int ToolUseCount { get; init; }

    /// <summary>Wall-clock milliseconds the upstream took.</summary>
    [JsonPropertyName("ms")] public long DurationMs { get; init; }

    /// <summary>True when the response was an event stream, decided from the body rather than the
    /// header. Counts are read from the terminal frame; zeros here mean the stream carried no totals
    /// or was cut by the capture ceiling.</summary>
    [JsonPropertyName("streamed")] public bool Streamed { get; init; }

    /// <summary>Bounded prefix of the newest user message, present only when prompt capture is on.</summary>
    [JsonPropertyName("prompt")] public string? PromptPrefix { get; init; }

    /// <summary>Bounded prefix of the first user message in the session, used as a readable title.</summary>
    [JsonPropertyName("sessionName")] public string? SessionName { get; init; }

    /// <summary>Request-body values named by <c>metadataFields</c>. Absent when none are configured
    /// or none resolved. Separates traffic a client sends on its own behalf from traffic the user
    /// asked for: Codex bills a hidden title-generation call against the same key as the real turn,
    /// and only <c>thread_source</c> tells them apart.</summary>
    [JsonPropertyName("meta")] public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

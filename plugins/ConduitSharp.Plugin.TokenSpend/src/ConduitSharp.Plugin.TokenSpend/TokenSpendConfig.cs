using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConduitSharp.Plugin.TokenSpend;

/// <summary>Configuration for the <c>token-spend</c> plugin.</summary>
public sealed record TokenSpendConfig
{
    /// <summary>Dotted response paths summed into the input-token column,
    /// e.g. <c>["usage.prompt_tokens"]</c> or <c>["usage.input_tokens"]</c>.</summary>
    [JsonPropertyName("inputFields")] public IReadOnlyList<string> InputFields { get; init; } = [];

    /// <summary>Paths taken back off the input column. Set this where a provider counts cached
    /// tokens inside its input total, so the column means fresh input everywhere. Clamps at zero.</summary>
    [JsonPropertyName("subtractInputFields")] public IReadOnlyList<string> SubtractInputFields { get; init; } = [];

    /// <summary>Dotted response paths summed into the output-token column.</summary>
    [JsonPropertyName("outputFields")] public IReadOnlyList<string> OutputFields { get; init; } = [];

    /// <summary>Paths taken back off the output column.</summary>
    [JsonPropertyName("subtractOutputFields")] public IReadOnlyList<string> SubtractOutputFields { get; init; } = [];

    /// <summary>Dotted response paths for cache-write tokens, kept separate because writing to the
    /// cache costs about 1.25x input.</summary>
    [JsonPropertyName("cacheWriteFields")] public IReadOnlyList<string> CacheWriteFields { get; init; } = [];

    /// <summary>Paths taken back off the cache-write column.</summary>
    [JsonPropertyName("subtractCacheWriteFields")] public IReadOnlyList<string> SubtractCacheWriteFields { get; init; } = [];

    /// <summary>Dotted response paths for cache-read tokens, which cost about a tenth of input.</summary>
    [JsonPropertyName("cacheReadFields")] public IReadOnlyList<string> CacheReadFields { get; init; } = [];

    /// <summary>Paths taken back off the cache-read column.</summary>
    [JsonPropertyName("subtractCacheReadFields")] public IReadOnlyList<string> SubtractCacheReadFields { get; init; } = [];

    /// <summary>Header identifying the caller (e.g. <c>x-api-key</c>). Only its salted hash is stored.</summary>
    [JsonPropertyName("keyHeader")] public string? KeyHeader { get; init; }

    /// <summary>JWT claim identifying the caller, used when <see cref="KeyHeader"/> is absent.</summary>
    [JsonPropertyName("keyClaim")] public string? KeyClaim { get; init; }

    /// <summary>Cap on response bytes buffered to find the usage fields. Default 1 MiB.</summary>
    [JsonPropertyName("maxResponseBytes")] public int MaxResponseBytes { get; init; } = 1024 * 1024;

    /// <summary>Cap on request bytes parsed for model and message array. Default 1 MiB; a long
    /// conversation exceeds this, in which case the parse yields nothing and the row still lands
    /// with its token counts.</summary>
    [JsonPropertyName("maxRequestBytes")] public int MaxRequestBytes { get; init; } = 1024 * 1024;

    /// <summary>Off by default. When on, the newest user message is stored, truncated to
    /// <see cref="MaxPromptChars"/>. This is the one setting that trades privacy for insight, so it
    /// is opt-in and nothing leaves the machine either way.</summary>
    [JsonPropertyName("capturePrompts")] public bool CapturePrompts { get; init; }

    /// <summary>Characters of the newest user message kept when <see cref="CapturePrompts"/> is on.</summary>
    [JsonPropertyName("maxPromptChars")] public int MaxPromptChars { get; init; } = 200;

    /// <summary>Tag names whose <c>&lt;tag&gt;…&lt;/tag&gt;</c> spans are cut out of a user message before it
    /// is read, e.g. <c>["system-reminder"]</c> for Claude Code, which prepends injected context to
    /// the message the human typed. Per route, because each client scaffolds differently.</summary>
    [JsonPropertyName("preambleTags")] public IReadOnlyList<string> PreambleTags { get; init; } = [];

    /// <summary>Opening text marking a user message as entirely the client's own, e.g.
    /// <c>["&lt;environment_context&gt;"]</c> for Codex. Matched after <see cref="PreambleTags"/> spans are
    /// cut. Such a message is skipped, so the prompt and session name on a row come from the first
    /// real one.</summary>
    [JsonPropertyName("preamblePrefixes")] public IReadOnlyList<string> PreamblePrefixes { get; init; } = [];

    /// <summary>Dotted request path to the conversation id the client already carries, e.g.
    /// <c>metadata.user_id.session_id</c> for Claude Code or <c>client_metadata.thread_id</c> for
    /// Codex. Both bury it inside a JSON document held in a string, which the path descends into.
    /// Name the leaf, not its parent: Claude's <c>user_id</c> object also holds a device id and an
    /// account uuid, neither of which should reach disk. Unset, or a path a body does not carry,
    /// falls back to hashing the first user message, which splits a conversation whenever the client
    /// compacts and merges conversations whose first message is a shared synthetic preamble.</summary>
    [JsonPropertyName("sessionField")] public string SessionField { get; init; } = "";

    /// <summary>Named dotted request paths copied onto each row under <c>meta</c>, e.g.
    /// <c>{"source": "client_metadata.x-codex-turn-metadata.thread_source"}</c>. A path may keep
    /// descending past a value that is itself a JSON document held in a string, which is how Codex
    /// ships its turn metadata. Values are truncated to 200 characters. Nothing is captured unless
    /// a path names it, so point these at metadata rather than at content.</summary>
    [JsonPropertyName("metadataFields")]
    public IReadOnlyDictionary<string, string> MetadataFields { get; init; }
        = new Dictionary<string, string>();

    internal static TokenSpendConfig From(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object
            ? raw.Deserialize<TokenSpendConfig>(JsonOptions) ?? new TokenSpendConfig()
            : new TokenSpendConfig();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ConduitSharp.Plugin.TokenSpend;

/// <summary>
/// Records what every LLM call cost, as durable history. It reads the same provider <c>usage</c>
/// block <c>token-rate-limit</c> reads, but instead of charging a window it writes one
/// <see cref="SpendRecord"/> per request to an <see cref="ISpendStore"/>, so the data is still there
/// weeks later to answer which habits burn tokens.
///
/// <para>Input, output, cache-write, and cache-read counts are kept as four separate fields rather
/// than summed, because they price differently and the split is what makes caching legible.</para>
///
/// <para>The response is buffered write-through, so the client receives bytes as they arrive while
/// the gateway keeps a bounded copy to parse. The request body is read too (hence
/// <see cref="ReadsRequestBody"/>), which is what supplies the model, the turn index, and the
/// session grouping: an Anthropic or OpenAI request carries the whole message array every turn, so
/// one intercepted request yields all three without the client cooperating.</para>
///
/// <para><b>Streaming is not parsed in this build.</b> An SSE response is not one JSON document, so
/// a <c>text/event-stream</c> reply is recorded with <see cref="SpendRecord.Streamed"/> set and zero
/// tokens. That is deliberate: the call is visible in the history as uncounted rather than missing
/// from it. Claude Code streams by default, so front a non-streaming endpoint to measure it.</para>
///
/// <para>routes.json config, variant <c>token-spend</c>:</para>
/// <code>
/// { "inputFields":  ["usage.prompt_tokens"],
///   "outputFields": ["usage.completion_tokens"],
///   "cacheWriteFields": ["usage.cache_creation_input_tokens"],
///   "cacheReadFields":  ["usage.cache_read_input_tokens"],
///   "keyHeader": "x-api-key", "capturePrompts": false }
/// </code>
/// </summary>
public sealed class TokenSpendPlugin : IPipelinePlugin
{
    public PluginName Name => PluginName.Custom;
    public string? Variant => "token-spend";
    public string Id => "token-spend";

    /// <summary>The request body carries the model, message count, and prompt text, none of which
    /// the response reports.</summary>
    public bool ReadsRequestBody => true;

    /// <summary>The response buffer held per request, reserved against the gateway's RAM budget.</summary>
    public int CaptureMemoryBytes(JsonElement config) => TokenSpendConfig.From(config).MaxResponseBytes;

    public void ValidateConfig(JsonElement config)
    {
        var c = TokenSpendConfig.From(config);
        if (c.InputFields.Count == 0 && c.OutputFields.Count == 0)
            throw new InvalidOperationException(
                "token-spend: set 'inputFields' and 'outputFields' to the dotted paths holding the token counts, "
                + "e.g. [\"usage.prompt_tokens\"] and [\"usage.completion_tokens\"].");
        if (c.MaxResponseBytes <= 0)
            throw new InvalidOperationException($"token-spend: 'maxResponseBytes' must be greater than zero (was {c.MaxResponseBytes}).");
        if (c.MaxRequestBytes <= 0)
            throw new InvalidOperationException($"token-spend: 'maxRequestBytes' must be greater than zero (was {c.MaxRequestBytes}).");
        if (c.MaxPromptChars <= 0)
            throw new InvalidOperationException($"token-spend: 'maxPromptChars' must be greater than zero (was {c.MaxPromptChars}).");
    }

    public async Task ExecuteAsync(HttpContext context, JsonElement configElement, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        var config = TokenSpendConfig.From(configElement);
        var store = context.RequestServices.GetRequiredService<ISpendStore>();

        var facts = await ReadRequestAsync(context, config);

        var originalBody = context.Response.Body;
        using var capture = new BoundedTeeStream(originalBody, config.MaxResponseBytes);
        context.Response.Body = capture;

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var record = new SpendRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Route = context.Items.TryGetValue("ConduitSharp.RouteId", out var r) && r is string id ? id : "",
            Model = facts.Model,
            Caller = store.HashCaller(ResolveClientKey(context, config)),
            SessionId = facts.SessionId,
            TurnIndex = facts.TurnIndex,
            ToolUseCount = facts.ToolUseCount,
            DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            Streamed = IsEventStream(context.Response.ContentType),
            PromptPrefix = config.CapturePrompts ? facts.PromptPrefix : null,
        };

        if (!record.Streamed)
            record = WithUsage(record, capture.Captured.Span, config);

        store.Add(record);
    }

    private static bool IsEventStream(string? contentType) =>
        contentType is not null && contentType.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase);

    private static SpendRecord WithUsage(SpendRecord row, ReadOnlySpan<byte> body, TokenSpendConfig config)
    {
        if (body.IsEmpty)
            return row;
        try
        {
            using var doc = JsonDocument.Parse(body.ToArray());
            var root = doc.RootElement;
            return row with
            {
                InputTokens = SumPaths(root, config.InputFields),
                OutputTokens = SumPaths(root, config.OutputFields),
                CacheWriteTokens = SumPaths(root, config.CacheWriteFields),
                CacheReadTokens = SumPaths(root, config.CacheReadFields),
            };
        }
        catch (JsonException)
        {
            return row;
        }
    }

    private static long SumPaths(JsonElement root, IReadOnlyList<string> paths)
    {
        long sum = 0;
        foreach (var path in paths)
            if (TryGetByPath(root, path, out var value))
                sum += value;
        return sum;
    }

    private static bool TryGetByPath(JsonElement root, string path, out long value)
    {
        value = 0;
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return false;
        }
        return current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out value);
    }

    private static async Task<RequestFacts> ReadRequestAsync(HttpContext context, TokenSpendConfig config)
    {
        if (!context.Request.Body.CanSeek)
            return RequestFacts.None;

        var buffer = ArrayPool<byte>.Shared.Rent(config.MaxRequestBytes);
        try
        {
            context.Request.Body.Position = 0;
            var length = await context.Request.Body.ReadAtLeastAsync(
                buffer.AsMemory(0, config.MaxRequestBytes), config.MaxRequestBytes, throwOnEndOfStream: false);
            context.Request.Body.Position = 0;

            return ParseRequest(buffer.AsSpan(0, length), config);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static RequestFacts ParseRequest(ReadOnlySpan<byte> body, TokenSpendConfig config)
    {
        if (body.IsEmpty)
            return RequestFacts.None;
        try
        {
            using var doc = JsonDocument.Parse(body.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return RequestFacts.None;

            var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? ""
                : "";

            if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                return RequestFacts.None with { Model = model };

            string? firstUserText = null;
            string? lastUserText = null;
            var tools = 0;

            foreach (var message in messages.EnumerateArray())
            {
                if (message.ValueKind != JsonValueKind.Object)
                    continue;

                var role = message.TryGetProperty("role", out var ro) && ro.ValueKind == JsonValueKind.String
                    ? ro.GetString()
                    : null;

                tools += ToolBlocks(message, role);

                if (role is not "user")
                    continue;
                var text = TextOf(message);
                if (text.Length == 0)
                    continue;
                firstUserText ??= text;
                lastUserText = text;
            }

            return new RequestFacts
            {
                Model = model,
                TurnIndex = messages.GetArrayLength(),
                ToolUseCount = tools,
                SessionId = firstUserText is null ? "" : ShortHash(firstUserText),
                PromptPrefix = lastUserText is null
                    ? null
                    : lastUserText[..Math.Min(lastUserText.Length, config.MaxPromptChars)],
            };
        }
        catch (JsonException)
        {
            return RequestFacts.None;
        }
    }

    private static int ToolBlocks(JsonElement message, string? role)
    {
        var count = 0;
        if (role is "tool")
            count++;
        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            count += calls.GetArrayLength();
        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object
                    && block.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString()?.StartsWith("tool", StringComparison.Ordinal) == true)
                    count++;
            }
        }
        return count;
    }

    private static string TextOf(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
            return "";
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array)
            return "";

        var text = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("text", out var t)
                && t.ValueKind == JsonValueKind.String)
            {
                if (text.Length > 0)
                    text.Append('\n');
                text.Append(t.GetString());
            }
        }
        return text.ToString();
    }

    private static string ShortHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 8)).ToLowerInvariant();

    private static string ResolveClientKey(HttpContext context, TokenSpendConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.KeyHeader)
            && context.Request.Headers.TryGetValue(config.KeyHeader, out var h)
            && !string.IsNullOrWhiteSpace(h.ToString()))
            return h.ToString();

        if (!string.IsNullOrWhiteSpace(config.KeyClaim)
            && ClaimFromBearer(context, config.KeyClaim!) is { } claim)
            return claim;

        return "global";
    }

    private static string? ClaimFromBearer(HttpContext context, string claim)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = header[prefix.Length..].Split('.');
        if (parts.Length < 2)
            return null;

        try
        {
            var payload = Convert.FromBase64String(PadBase64Url(parts[1]));
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty(claim, out var value))
                return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string PadBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
    }
}

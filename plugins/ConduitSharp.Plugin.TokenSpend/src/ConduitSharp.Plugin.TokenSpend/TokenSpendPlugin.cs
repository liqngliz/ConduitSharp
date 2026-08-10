using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ConduitSharp.Plugin.TokenSpend;

/// <summary>
/// Records what every LLM call cost, writing one <see cref="SpendRecord"/> per request to an
/// <see cref="ISpendStore"/>. Input, output, cache-write and cache-read are separate fields.
///
/// <para>Streamed replies are read too: <see cref="SpendRecord.Streamed"/> is set from the body
/// rather than the header, and usage comes from the terminal SSE frame. A stream carrying no totals
/// still lands as streamed with zeros, so an uncounted call is visible rather than absent.</para>
///
/// <para>routes.json config, variant <c>token-spend</c>:</para>
/// <code>
/// { "inputFields":         ["usage.input_tokens"],
///   "subtractInputFields": ["usage.input_tokens_details.cached_tokens"],
///   "outputFields":        ["usage.output_tokens"],
///   "cacheReadFields":     ["usage.input_tokens_details.cached_tokens"],
///   "cacheWriteFields":    ["usage.input_tokens_details.cache_write_tokens"],
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

        var body = Decode(capture.Captured.ToArray(), context.Response.Headers.ContentEncoding.ToString());

        var record = new SpendRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Route = context.Items.TryGetValue("ConduitSharp.RouteId", out var r) && r is string id ? id : "",
            Model = facts.Model,
            Caller = store.HashCaller(ResolveClientKey(context, config)),
            // Same expression BodyCaptureToFilePlugin uses, fallback included, so the two files
            // carry an identical id for one request whether or not a tracer is listening.
            TraceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            SessionId = facts.SessionId,
            TurnIndex = facts.TurnIndex,
            ToolUseCount = facts.ToolUseCount,
            DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            Streamed = IsEventStream(context.Response.ContentType) || LooksLikeEventStream(body),
            PromptPrefix = config.CapturePrompts ? facts.PromptPrefix : null,
            SessionName = config.CapturePrompts ? facts.SessionName : null,
            Metadata = facts.Metadata,
        };

        record = record.Streamed
            ? await WithStreamedUsageAsync(record, body, config)
            : WithUsage(record, body, config);

        store.Add(record);
        var broadcaster = context.RequestServices.GetService(typeof(SpendBroadcaster)) as SpendBroadcaster;
        broadcaster?.Publish(record);
    }

    /// <summary>
    /// Undoes the upstream's content coding. An upstream honours the client's <c>Accept-Encoding</c>,
    /// so the captured bytes are frequently gzip or brotli rather than text: Anthropic compresses its
    /// SSE stream, and without this every Claude row reads zero tokens.
    /// </summary>
    private static byte[] Decode(byte[] body, string contentEncoding)
    {
        if (body.Length == 0 || contentEncoding.Length == 0)
            return body;

        using var source = new MemoryStream(body);
        using Stream? decoder =
            contentEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase) ? new GZipStream(source, CompressionMode.Decompress)
            : contentEncoding.Contains("br", StringComparison.OrdinalIgnoreCase) ? new BrotliStream(source, CompressionMode.Decompress)
            : contentEncoding.Contains("deflate", StringComparison.OrdinalIgnoreCase) ? new ZLibStream(source, CompressionMode.Decompress)
            : null;
        if (decoder is null)
            return body;

        using var output = new MemoryStream();
        try
        {
            decoder.CopyTo(output);
        }
        catch (InvalidDataException)
        {
            // The capture ceiling cut the stream mid-block. CopyTo writes as it goes, so whatever
            // decoded before the break is kept.
        }
        return output.Length > 0 ? output.ToArray() : body;
    }

    private static bool IsEventStream(string? contentType) =>
        contentType is not null && contentType.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sniffs the captured bytes for SSE framing. The header is the declaration; this is the
    /// evidence, and it is what makes the row honest when the two disagree.</summary>
    private static bool LooksLikeEventStream(ReadOnlySpan<byte> body)
    {
        if (body.Length < 6)
            return false;
        var head = Encoding.UTF8.GetString(body[..Math.Min(body.Length, 512)]);
        return head.StartsWith("event:", StringComparison.Ordinal)
            || head.StartsWith("data:", StringComparison.Ordinal)
            || head.Contains("\nevent:", StringComparison.Ordinal)
            || head.Contains("\ndata:", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads usage out of an SSE body with <see cref="SseParser"/> from the framework rather than a
    /// hand-rolled line scanner. Providers put the totals in the terminal frame, so every frame is
    /// tried and the last one carrying a non-zero total wins.
    /// </summary>
    private static async Task<SpendRecord> WithStreamedUsageAsync(
        SpendRecord row, byte[] body, TokenSpendConfig config)
    {
        if (body.Length == 0)
            return row;

        // A capture bounded by maxResponseBytes rarely ends on a frame boundary, and the parser only
        // dispatches an event once it sees the blank line that terminates it. Without this the last
        // frame is dropped, which is precisely the one holding the totals.
        var terminated = new byte[body.Length + 2];
        body.CopyTo(terminated, 0);
        terminated[^2] = (byte)'\n';
        terminated[^1] = (byte)'\n';

        var result = row;
        try
        {
            using var stream = new MemoryStream(terminated);
            var parser = SseParser.Create(stream, (_, data) => Encoding.UTF8.GetString(data));
            await foreach (var frame in parser.EnumerateAsync())
            {
                var candidate = UsageFromFrame(result, frame.Data, config);
                if (candidate is not null)
                    result = candidate;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // A frame cut mid-JSON by the capture ceiling. Keep whatever earlier frames yielded.
        }
        return result;
    }

    /// <summary>Applies the configured paths to one frame, at its root and under a <c>response</c>
    /// wrapper, since Anthropic puts usage at the top of the frame and OpenAI nests it one deeper.
    /// Returns null when the frame carries no usable totals.</summary>
    private static SpendRecord? UsageFromFrame(SpendRecord row, string data, TokenSpendConfig config)
    {
        if (string.IsNullOrWhiteSpace(data) || data[0] != '{')
            return null;
        try
        {
            using var doc = JsonDocument.Parse(data);
            foreach (var root in Roots(doc.RootElement))
            {
                var input = Column(root, config.InputFields, config.SubtractInputFields);
                var output = Column(root, config.OutputFields, config.SubtractOutputFields);
                if (input == 0 && output == 0)
                    continue;

                // Per column, the largest total any frame has reported so far. Anthropic splits its
                // usage across two frames — `message_start` carries the input and cache columns with
                // a placeholder output of 1, `message_delta` carries the real output and no input —
                // so last-frame-wins would drop whichever half arrived first.
                // ponytail: assumes a provider reports running or final totals, not per-frame deltas.
                // A delta-reporting provider would undercount; none of the three routes here does.
                return row with
                {
                    InputTokens = Math.Max(row.InputTokens, input),
                    OutputTokens = Math.Max(row.OutputTokens, output),
                    CacheWriteTokens = Math.Max(
                        row.CacheWriteTokens, Column(root, config.CacheWriteFields, config.SubtractCacheWriteFields)),
                    CacheReadTokens = Math.Max(
                        row.CacheReadTokens, Column(root, config.CacheReadFields, config.SubtractCacheReadFields)),
                    ServedModel = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString() ?? row.ServedModel
                        : row.ServedModel,
                };
            }
        }
        catch (JsonException)
        {
        }
        return null;

        // Anthropic puts usage at the frame root on `message_delta` and one level down under
        // `message` on `message_start`; OpenAI nests it under `response`.
        static IEnumerable<JsonElement> Roots(JsonElement frame)
        {
            yield return frame;
            if (frame.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
                yield return response;
            if (frame.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                yield return message;
        }
    }

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
                InputTokens = Column(root, config.InputFields, config.SubtractInputFields),
                OutputTokens = Column(root, config.OutputFields, config.SubtractOutputFields),
                CacheWriteTokens = Column(root, config.CacheWriteFields, config.SubtractCacheWriteFields),
                CacheReadTokens = Column(root, config.CacheReadFields, config.SubtractCacheReadFields),
                ServedModel = root.TryGetProperty("model", out var served) && served.ValueKind == JsonValueKind.String
                    ? served.GetString() ?? ""
                    : "",
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

    /// <summary>One column: what its paths add, less what its subtract paths take back. Clamped at
    /// zero, because a column that subtracts more than it adds is a misconfiguration and a negative
    /// token count is never meaningful.</summary>
    private static long Column(JsonElement root, IReadOnlyList<string> add, IReadOnlyList<string> subtract)
    {
        var total = SumPaths(root, add) - SumPaths(root, subtract);
        return total < 0 ? 0 : total;
    }

    private const int MaxMetadataValueChars = 200;

    /// <summary>Resolves the configured metadata paths against the request body. Returns null rather
    /// than an empty map so a row carries no <c>meta</c> key at all when nothing was asked for.</summary>
    private static IReadOnlyDictionary<string, string>? ReadMetadata(
        JsonElement root, IReadOnlyDictionary<string, string> paths)
    {
        if (paths.Count == 0)
            return null;

        Dictionary<string, string>? found = null;
        foreach (var (name, path) in paths)
        {
            if (!TryGetStringByPath(root, path, out var value))
                continue;
            (found ??= [])[name] = value.Length <= MaxMetadataValueChars
                ? value
                : value[..MaxMetadataValueChars];
        }
        return found;
    }

    /// <summary>Strips the client's own scaffolding out of a user message, used only to pick which
    /// message names the session, never to build the prompt itself. Two shapes, because clients use
    /// both: a tagged span the client wraps around injected context, cut wherever it appears since
    /// Claude Code prepends it to a real message; and a whole message that is nothing but preamble,
    /// matched on its opening words and returned empty so the caller skips it. An unclosed tag takes
    /// the rest of the text with it, which is what a span cut short by the request cap looks like.</summary>
    private static string WithoutPreamble(string text, TokenSpendConfig config)
    {
        foreach (var tag in config.PreambleTags)
        {
            var open = $"<{tag}>";
            var close = $"</{tag}>";
            int start;
            while ((start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = text.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
                text = end < 0 ? text[..start] : text.Remove(start, end - start + close.Length);
            }
        }

        text = text.Trim();
        foreach (var prefix in config.PreamblePrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return "";
        }
        return text;
    }

    private const int MaxSessionIdChars = 64;

    /// <summary>The conversation id the client already carries, or null when no path is configured or
    /// the path resolves to nothing, which leaves the caller on its first-message hash. A UUID is 36
    /// characters; the cap is here because the path is user-supplied and could name anything.</summary>
    private static string? ReadSessionId(JsonElement root, string path)
    {
        if (path.Length == 0 || !TryGetStringByPath(root, path, out var value))
            return null;
        return value.Length <= MaxSessionIdChars ? value : value[..MaxSessionIdChars];
    }

    /// <summary>Same dotted walk as <see cref="TryGetByPath"/>, but yields text and re-parses a
    /// string that a path continues through. Codex packs a whole JSON document into the
    /// <c>x-codex-turn-metadata</c> string, so stopping at the string would put the interesting
    /// fields out of reach.</summary>
    private static bool TryGetStringByPath(JsonElement root, string path, out string value)
    {
        value = "";
        var current = root;
        List<JsonDocument>? nested = null;
        try
        {
            foreach (var segment in path.Split('.'))
            {
                if (current.ValueKind == JsonValueKind.String)
                {
                    var text = current.GetString();
                    if (text is null)
                        return false;
                    JsonDocument document;
                    try { document = JsonDocument.Parse(text); }
                    catch (JsonException) { return false; }
                    (nested ??= []).Add(document);
                    current = document.RootElement;
                }
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                    return false;
            }

            // Every branch here allocates its own string, so the value outlives the documents the
            // finally block disposes.
            value = current.ValueKind switch
            {
                JsonValueKind.String => current.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => current.GetRawText(),
            };
            return value.Length > 0;
        }
        finally
        {
            if (nested is not null)
                foreach (var document in nested)
                    document.Dispose();
        }
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

            // Resolved before the message array is required, so a body the parse otherwise gives up
            // on still carries its metadata and its session.
            var metadata = ReadMetadata(root, config.MetadataFields);
            var session = ReadSessionId(root, config.SessionField);

            // Chat Completions and Anthropic call it "messages"; the Responses API, which is what
            // Codex speaks, calls it "input". Same role/content shape either way.
            if ((!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                && (!root.TryGetProperty("input", out messages) || messages.ValueKind != JsonValueKind.Array))
                return RequestFacts.None with { Model = model, Metadata = metadata, SessionId = session ?? "" };

            string? firstUserText = null;
            string? lastUserText = null;
            var tools = 0;

            // Responses declares the tool catalogue at the top level rather than inside an item.
            if (root.TryGetProperty("tools", out var toolCatalogue) && toolCatalogue.ValueKind == JsonValueKind.Array)
                tools += toolCatalogue.GetArrayLength();

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

                // Prompt keeps the message verbatim: it is the newest turn, already known to be
                // real rather than scaffolding, so stripping would just cut what the human typed.
                var raw = TextOf(message);
                if (raw.Length > 0)
                    lastUserText = raw;

                // Session naming needs the opposite: preamble-only messages (a Codex environment
                // block, a Claude system-reminder with nothing else) must not become the name.
                var cleaned = WithoutPreamble(raw, config);
                if (cleaned.Length > 0)
                    firstUserText ??= cleaned;
            }

            return new RequestFacts
            {
                Model = model,
                TurnIndex = messages.GetArrayLength(),
                ToolUseCount = tools,
                SessionId = session ?? (firstUserText is null ? "" : ShortHash(firstUserText)),
                SessionName = firstUserText is null
                    ? null
                    : firstUserText[..Math.Min(firstUserText.Length, config.MaxPromptChars)],
                PromptPrefix = lastUserText is null
                    ? null
                    : lastUserText[..Math.Min(lastUserText.Length, config.MaxPromptChars)],
                Metadata = metadata,
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

        // Responses represents a call as an item typed function_call / function_call_output, and
        // ships its own tool catalogue as an additional_tools item.
        if (message.TryGetProperty("type", out var itemType) && itemType.ValueKind == JsonValueKind.String)
        {
            var t = itemType.GetString();
            if (t is "function_call" or "function_call_output" or "custom_tool_call")
                count++;
            else if (t is "additional_tools"
                     && message.TryGetProperty("tools", out var extra) && extra.ValueKind == JsonValueKind.Array)
                count += extra.GetArrayLength();
        }

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

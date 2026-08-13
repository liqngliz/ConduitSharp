using System.Buffers;
using System.Text;

namespace ConduitSharp.Core.Logging;

/// <summary>
/// Escapes line-break characters in values that reach a log message. A request path or body carries
/// whatever the caller sent, and a line break inside a plain-text log entry is a forged log entry
/// (CWE-117). .NET ships no log sanitizer; CodeQL's documented fix for <c>cs/log-forging</c> is
/// stripping line breaks with <c>String.Replace</c>.
///
/// <c>JavaScriptEncoder</c> covers the same characters, but also escapes every quote and angle
/// bracket, which turns a captured JSON body into noise. Escaping only the breaks keeps bodies readable.
///
/// U+0085, U+2028 and U+2029 are included: many log viewers treat them as line breaks, so
/// CR/LF-only stripping is bypassable.
/// </summary>
public static class LogSanitizer
{
    private const char Nel     = (char)0x0085;  // NEXT LINE
    private const char LineSep = (char)0x2028;  // LINE SEPARATOR
    private const char ParaSep = (char)0x2029;  // PARAGRAPH SEPARATOR

    private static readonly SearchValues<char> Breaks =
        SearchValues.Create(['\r', '\n', Nel, LineSep, ParaSep]);

    /// <summary>Returns <paramref name="value"/> with every line-break character replaced by a
    /// literal escape. Clean input returns as-is after one vectorized scan, with no allocation.</summary>
    public static string ForLog(this string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var first = value.AsSpan().IndexOfAny(Breaks);
        if (first < 0) return value;

        var sb = new StringBuilder(value.Length + 16);
        sb.Append(value.AsSpan(0, first));
        foreach (var c in value.AsSpan(first))
        {
            switch (c)
            {
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case Nel:
                case LineSep:
                case ParaSep: sb.Append("\\u").Append(((int)c).ToString("x4")); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

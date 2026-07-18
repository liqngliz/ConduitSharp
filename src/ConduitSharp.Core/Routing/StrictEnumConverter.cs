using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConduitSharp.Core.Routing;

/// <summary>
/// A strict JSON converter for any <typeparamref name="T"/> enum.
/// Accepts three input casing styles transparently:
///   • kebab-case  ("jwt-auth"    → JwtAuth)
///   • PascalCase  ("RoundRobin"  → RoundRobin)
///   • UPPERCASE   ("GET"         → Get → HttpVerb.Get)
/// Throws <see cref="JsonException"/> with a clear message listing all valid
/// values when the string does not map to any enum member.
/// </summary>
/// <typeparam name="T">The target enum type.</typeparam>
public sealed class StrictEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    /// <inheritdoc/>
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();

        if (string.IsNullOrWhiteSpace(raw))
            throw Throw(raw);

        // 1. Direct case-insensitive match (handles PascalCase and UPPERCASE inputs).
        if (Enum.TryParse<T>(raw, ignoreCase: true, out var direct))
            return direct;

        // 2. Kebab-case → PascalCase conversion ("jwt-auth" → "JwtAuth").
        var pascalCase = KebabToPascal(raw!);
        if (Enum.TryParse<T>(pascalCase, ignoreCase: false, out var fromKebab))
            return fromKebab;

        throw Throw(raw);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());

    // -----------------------------------------------------------------------

    private static string KebabToPascal(string kebab)
        => string.Concat(
            kebab.Split('-')
                 .Select(segment => segment.Length == 0
                     ? string.Empty
                     : char.ToUpperInvariant(segment[0]) + segment[1..].ToLowerInvariant()));

    private static Exception Throw(string? raw)
    {
        var valid = string.Join(", ", Enum.GetNames<T>());
        throw new JsonException(
            $"'{raw}' is not a valid value for {typeof(T).Name}. Valid values: {valid}.");
    }
}

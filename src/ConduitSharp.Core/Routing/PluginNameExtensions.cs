using System.Text;

namespace ConduitSharp.Core.Routing;

/// <summary>
/// Maps <see cref="PluginName"/> enum members to their canonical kebab-case string —
/// the same identity a plugin declares via <c>IPipelinePlugin.Id</c> and the same spelling
/// <c>routes.json</c> uses (e.g. <c>RateLimit</c> → <c>"rate-limit"</c>).
///
/// Built-in plugins use <c>Name.ToId()</c> instead of hand-typing the string, so the two
/// can never drift apart. External/<see cref="PluginName.Custom"/> plugins still declare
/// <c>Id</c> explicitly — <c>PluginName.Custom.ToId()</c> is just <c>"custom"</c> for all of
/// them, and <c>Variant</c> is what actually distinguishes one custom plugin from another.
/// </summary>
public static class PluginNameExtensions
{
    /// <summary>Converts a <see cref="PluginName"/> member to its canonical kebab-case string.</summary>
    public static string ToId(this PluginName name) => PascalToKebab(name.ToString());

    private static string PascalToKebab(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 4);

        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

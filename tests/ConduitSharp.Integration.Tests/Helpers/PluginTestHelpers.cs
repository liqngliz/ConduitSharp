using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ConduitSharp.Integration.Tests.Helpers;

internal static class PluginTestHelpers
{
    // 32 bytes — HS256 validation requires a key of at least 256 bits.
    internal static readonly string TestSecretBase64 =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("integration-test-secret-key!!!!!"));

    internal static string BuildHs256Token(
        string secretBase64,
        long? expOffset = null,
        string? issuer   = null,
        string? audience = null,
        Dictionary<string, object?>? extraClaims = null)
    {
        var header  = B64U("""{"alg":"HS256","typ":"JWT"}""");
        var claims  = new Dictionary<string, object?> { ["sub"] = "test" };
        if (expOffset.HasValue)
            claims["exp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expOffset.Value;
        if (issuer   is not null) claims["iss"] = issuer;
        if (audience is not null) claims["aud"] = audience;
        if (extraClaims is not null)
            foreach (var (claimName, value) in extraClaims)
                claims[claimName] = value;
        var payload = B64U(JsonSerializer.Serialize(claims));

        var key  = Convert.FromBase64String(PadBase64(secretBase64));
        using var hmac = new HMACSHA256(key);
        var sig  = B64U(hmac.ComputeHash(Encoding.ASCII.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{sig}";
    }

    internal static string Sha256Hex(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    private static string B64U(string s) => B64U(Encoding.UTF8.GetBytes(s));
    private static string B64U(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string PadBase64(string s) => (s.Length % 4) switch
    {
        2 => s + "==", 3 => s + "=", _ => s
    };
}

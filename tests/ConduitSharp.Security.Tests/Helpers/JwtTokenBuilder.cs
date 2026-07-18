using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ConduitSharp.Security.Tests.Helpers;

internal static class JwtTokenBuilder
{
    /// <summary>A stable 32-byte secret used across tests (HS256 requires ≥ 256 bits).</summary>
    internal static readonly string DefaultSecretBase64 =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("conduit-sharp-test-secret-key!!!"));

    /// <summary>Builds a HS256 JWT with the given claims.</summary>
    internal static string Build(
        string secretBase64,
        string? issuer      = null,
        string? audience    = null,
        string[]? audiences = null,
        DateTimeOffset? exp = null,
        DateTimeOffset? nbf = null,
        string algorithm    = "HS256",
        Dictionary<string, object?>? extraClaims = null)
    {
        var headerJson  = JsonSerializer.Serialize(new { alg = algorithm, typ = "JWT" });
        var claims      = BuildClaims(issuer, audience, audiences, exp, nbf);
        if (extraClaims is not null)
            foreach (var (key, value) in extraClaims)
                claims[key] = value;
        var payloadJson = JsonSerializer.Serialize(claims);

        var header  = Base64UrlEncode(headerJson);
        var payload = Base64UrlEncode(payloadJson);

        var signature = algorithm == "HS256"
            ? ComputeHs256(secretBase64, header, payload)
            : "invalidsignature";

        return $"{header}.{payload}.{signature}";
    }

    internal static string ValidToken(
        string? secretBase64 = null,
        string? issuer       = null,
        string? audience     = null,
        Dictionary<string, object?>? extraClaims = null) =>
        Build(
            secretBase64 ?? DefaultSecretBase64,
            issuer:      issuer,
            audience:    audience,
            exp:         DateTimeOffset.UtcNow.AddHours(1),
            extraClaims: extraClaims);

    internal static string ExpiredToken(string? secretBase64 = null) =>
        Build(secretBase64 ?? DefaultSecretBase64, exp: DateTimeOffset.UtcNow.AddHours(-1));

    internal static string NotYetValidToken(string? secretBase64 = null) =>
        Build(
            secretBase64 ?? DefaultSecretBase64,
            exp: DateTimeOffset.UtcNow.AddHours(2),
            nbf: DateTimeOffset.UtcNow.AddHours(1));

    /// <summary>Valid structure but signed with a different secret.</summary>
    internal static string BadSignatureToken() =>
        Build(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("a-completely-different-secret!!")),
            exp: DateTimeOffset.UtcNow.AddHours(1));

    // -------------------------------------------------------------------------

    private static Dictionary<string, object?> BuildClaims(
        string? iss, string? aud, string[]? auds, DateTimeOffset? exp, DateTimeOffset? nbf)
    {
        var d = new Dictionary<string, object?> { ["sub"] = "test-subject" };
        if (iss  is not null) d["iss"] = iss;
        if (aud  is not null) d["aud"] = aud;
        if (auds is not null) d["aud"] = auds;
        if (exp  is not null) d["exp"] = exp.Value.ToUnixTimeSeconds();
        if (nbf  is not null) d["nbf"] = nbf.Value.ToUnixTimeSeconds();
        return d;
    }

    private static string ComputeHs256(string secretBase64, string header, string payload)
    {
        var keyBytes = Convert.FromBase64String(PadBase64(secretBase64));
        var input    = Encoding.ASCII.GetBytes($"{header}.{payload}");
        using var hmac = new HMACSHA256(keyBytes);
        return Base64UrlEncode(hmac.ComputeHash(input));
    }

    private static string Base64UrlEncode(string input) =>
        Base64UrlEncode(Encoding.UTF8.GetBytes(input));

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string PadBase64(string s) => (s.Length % 4) switch
    {
        2 => s + "==",
        3 => s + "=",
        _ => s
    };
}

using System.Security.Cryptography;
using System.Text;

namespace ConduitSharp.Security.ApiKey;

/// <summary>
/// Stateless API key validator. Both sides are SHA-256 hashed before the
/// constant-time comparison so neither key content nor key <em>length</em> leaks
/// through timing (<c>FixedTimeEquals</c> returns immediately on a length mismatch,
/// which would otherwise reveal each configured key's length).
/// </summary>
public static class ApiKeyAuthHandler
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="suppliedKey"/> matches any entry in
    /// <paramref name="allowedKeys"/>.
    /// </summary>
    public static bool IsValid(string suppliedKey, IEnumerable<string> allowedKeys)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        foreach (var key in allowedKeys)
        {
            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            if (CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash))
                return true;
        }
        return false;
    }
}

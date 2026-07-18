using System.Security.Cryptography;
using System.Text;

namespace ConduitSharp.Security.ApiKey;

/// <summary>
/// Validates an API key by comparing SHA-256(suppliedKey) against a stored hash.
/// The config holds hex-encoded SHA-256 hashes, never the raw keys.
///
/// This protects against config-file leaks: a leaked hash cannot be used directly
/// as an API key. Because API keys must be long random strings (32+ bytes of
/// entropy), SHA-256 is sufficient — rainbow tables are infeasible at that key
/// length, so bcrypt/Argon2 overhead is unnecessary.
///
/// To pre-compute a hash for storage in routes.json:
///   echo -n "my-raw-key" | sha256sum
/// or in C#:
///   Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("my-raw-key"))).ToLowerInvariant()
/// </summary>
public static class ApiKeyAuthHashedHandler
{
    /// <summary>
    /// Returns <c>true</c> when SHA-256(<paramref name="suppliedKey"/>) matches
    /// any entry in <paramref name="allowedHashes"/> (lowercase hex strings).
    /// Comparison is constant-time to resist timing attacks.
    /// </summary>
    public static bool IsValid(string suppliedKey, IEnumerable<string> allowedHashes)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));

        foreach (var hexHash in allowedHashes)
        {
            byte[] expected;
            try { expected = Convert.FromHexString(hexHash); }
            catch (FormatException) { continue; } // skip malformed entries rather than throw

            if (expected.Length == suppliedHash.Length &&
                CryptographicOperations.FixedTimeEquals(suppliedHash, expected))
                return true;
        }

        return false;
    }
}

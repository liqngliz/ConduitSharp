using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConduitSharp.Security.Jwt;

/// <summary>
/// Configuration for the <c>jwt-auth</c> plugin.
/// Validates HS256 Bearer JWTs. Place inside the route's <c>"config"</c> block.
///
/// Lives apart from <see cref="JwtAuthPlugin"/> because both the plugin and
/// <see cref="JwtAuthHandler"/> read it: with the records declared in the plugin's file, the
/// handler's dependency on them pointed back at the plugin that depends on the handler.
/// </summary>
/// <example>
/// <code>
/// {
///   "name": "jwt-auth",
///   "order": 1,
///   "enabled": true,
///   "config": {
///     // base64 of the raw secret bytes, e.g. base64("demo-signing-key-conduitsharp-example-32ch")
///     "signingKey": "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo",
///     "algorithm": "HS256",
///     "issuer":    "https://auth.example.com",
///     "audience":  "my-api"
///   }
/// }
/// </code>
/// </example>
public sealed record JwtProviderConfig
{
    [JsonPropertyName("signingKey")] public string  SigningKey { get; init; } = "";
    [JsonPropertyName("algorithm")]  public string  Algorithm  { get; init; } = "HS256";
    [JsonPropertyName("issuer")]     public string? Issuer     { get; init; }
    [JsonPropertyName("audience")]   public string? Audience   { get; init; }
    [JsonPropertyName("requiredClaims")] public List<RequiredClaim>? RequiredClaims { get; init; }
}

public sealed record JwtAuthConfig
{
    [JsonPropertyName("signingKey")] public string? SigningKey { get; init; }
    [JsonPropertyName("algorithm")]  public string  Algorithm  { get; init; } = "HS256";
    [JsonPropertyName("issuer")]     public string? Issuer     { get; init; }
    [JsonPropertyName("audience")]   public string? Audience   { get; init; }
    [JsonPropertyName("requiredClaims")] public List<RequiredClaim>? RequiredClaims { get; init; }

    /// <summary>List of identity providers. If specified, the token is evaluated against each until one succeeds.</summary>
    [JsonPropertyName("providers")]  public List<JwtProviderConfig>? Providers { get; init; }

    [JsonIgnore]
    public IReadOnlyList<JwtProviderConfig> EffectiveProviders
    {
        get
        {
            if (Providers is { Count: > 0 }) return Providers;
            return [ new JwtProviderConfig {
                SigningKey = SigningKey!,
                Algorithm = Algorithm,
                Issuer = Issuer,
                Audience = Audience,
                RequiredClaims = RequiredClaims
            } ];
        }
    }

    public static JwtAuthConfig From(JsonElement raw)
    {
        var config = raw.Deserialize<JwtAuthConfig>(JsonOptions)
            ?? throw new InvalidOperationException("jwt-auth plugin config is null or invalid.");

        if (config.Providers is { Count: > 0 })
        {
            foreach (var p in config.Providers)
            {
                ValidateSigningKey(p.SigningKey);
                RequiredClaim.ValidateAll(p.RequiredClaims);
            }
        }
        else
        {
            ValidateSigningKey(config.SigningKey);
            RequiredClaim.ValidateAll(config.RequiredClaims);
        }

        return config;
    }

    private static void ValidateSigningKey(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("jwt-auth: 'signingKey' is required in plugin config.");

        byte[] keyBytes;
        try
        {
            var padded = (signingKey.Length % 4) switch
            {
                2 => signingKey + "==",
                3 => signingKey + "=",
                _ => signingKey
            };
            keyBytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "jwt-auth: 'signingKey' must be the base64 encoding of the raw secret bytes " +
                "(the configured value is not valid base64).");
        }

        if (keyBytes.Length < 32)
            throw new InvalidOperationException(
                $"jwt-auth: 'signingKey' must decode to at least 32 bytes (256 bits) for HS256; " +
                $"it decodes to {keyBytes.Length} bytes.");
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
}

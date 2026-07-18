namespace ConduitSharp.Security.Jwt;

/// <summary>
/// Pure, stateless Bearer token extraction from an Authorization header value.
/// Extracted so both <see cref="JwtAuthPlugin"/> and <see cref="JwksJwtAuthPlugin"/>
/// share one implementation and it can be tested and swapped independently.
/// </summary>
public static class BearerTokenExtractor
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Returns <c>(token, null)</c> on success or <c>(null, errorMessage)</c> on failure.
    /// </summary>
    public static (string? Token, string? Error) Extract(string? authHeader)
    {
        if (string.IsNullOrWhiteSpace(authHeader))
            return (null, "Authorization header is required.");

        if (!authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return (null, "Bearer token is required.");

        return (authHeader[BearerPrefix.Length..].Trim(), null);
    }
}

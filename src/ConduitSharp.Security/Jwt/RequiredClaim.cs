using System.Text.Json.Serialization;

namespace ConduitSharp.Security.Jwt;

/// <summary>
/// One claim-based authorization rule evaluated against a verified JWT's claims,
/// after signature/exp/nbf/iss/aud have already passed. Place inside a
/// <c>jwt-auth</c> or <c>jwks-jwt-auth</c> plugin config's <c>"requiredClaims"</c> array —
/// every entry must pass (logical AND) or the request is short-circuited with 403.
///
/// Exactly one of <see cref="Equals"/>, <see cref="AnyOf"/>, <see cref="AllOf"/> may be
/// set; omitting all three makes the rule an existence-only check (the claim must be
/// present, any value).
/// </summary>
/// <example>
/// <code>
/// "requiredClaims": [
///   { "claim": "roles", "anyOf": ["Read", "Admin"] },
///   { "claim": "scp", "allOf": ["reports.read"], "delimiter": " " },
///   { "claim": "realm_access.roles", "anyOf": ["erp"] },
///   { "claim": "email_verified", "equals": "true" },
///   { "claim": "hd" }
/// ]
/// </code>
/// </example>
public sealed record RequiredClaim
{
    /// <summary>
    /// Claim name. Looked up as a literal top-level property name first (so a namespaced
    /// claim containing dots, e.g. Auth0's <c>https://example.com/roles</c>, matches
    /// directly); if no literal match exists, the name is split on <c>.</c> and traversed
    /// as a path into nested objects (e.g. Keycloak's <c>realm_access.roles</c>). Required.
    /// </summary>
    [JsonPropertyName("claim")] public required string Claim { get; init; }

    /// <summary>Claim value (as a string) must equal this exactly. Mutually exclusive with <see cref="AnyOf"/>/<see cref="AllOf"/>.</summary>
    [JsonPropertyName("equals")] public string? EqualsValue { get; init; }

    /// <summary>Claim's value set must intersect this list. Mutually exclusive with <see cref="Equals"/>/<see cref="AllOf"/>.</summary>
    [JsonPropertyName("anyOf")] public List<string>? AnyOf { get; init; }

    /// <summary>Claim's value set must contain every entry in this list. Mutually exclusive with <see cref="Equals"/>/<see cref="AnyOf"/>.</summary>
    [JsonPropertyName("allOf")] public List<string>? AllOf { get; init; }

    /// <summary>
    /// When the claim value is a single (non-array) string, split it on this delimiter
    /// before matching — e.g. <c>" "</c> for a space-delimited OAuth <c>scp</c>/<c>scope</c>
    /// claim. Ignored for array-valued or non-string claims.
    /// </summary>
    [JsonPropertyName("delimiter")] public string? Delimiter { get; init; }

    /// <summary>Validates this rule's shape, throwing on a config mistake. Called at startup and on admin reload.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Claim))
            throw new InvalidOperationException("requiredClaims: 'claim' must be non-empty.");

        var matcherCount = (EqualsValue is not null ? 1 : 0) + (AnyOf is not null ? 1 : 0) + (AllOf is not null ? 1 : 0);
        if (matcherCount > 1)
            throw new InvalidOperationException(
                $"requiredClaims: claim '{Claim}' must specify at most one of 'equals', 'anyOf', 'allOf'.");

        if (AnyOf is { Count: 0 })
            throw new InvalidOperationException($"requiredClaims: claim '{Claim}' 'anyOf' must not be empty.");
        if (AllOf is { Count: 0 })
            throw new InvalidOperationException($"requiredClaims: claim '{Claim}' 'allOf' must not be empty.");
    }

    /// <summary>Validates every rule in <paramref name="requiredClaims"/>, or no-ops when null/empty.</summary>
    public static void ValidateAll(IEnumerable<RequiredClaim>? requiredClaims)
    {
        if (requiredClaims is null) return;
        foreach (var claim in requiredClaims) claim.Validate();
    }
}

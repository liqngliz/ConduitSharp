using System.Text.Json;

namespace ConduitSharp.Security.Jwt;

/// <summary>
/// Pure, stateless claim-based authorization check, run after a JWT has already passed
/// signature/exp/nbf/iss/aud validation. Distinct from token validation (whose failures
/// are 401 — the token itself is invalid) because a failure here means the token is
/// valid but the caller lacks permission — the plugin maps it to 403.
///
/// Returns <c>null</c> on success or an error string identifying the failing claim.
/// </summary>
public static class RequiredClaimsValidator
{
    /// <summary>Runs every rule against <paramref name="claims"/>, returning the first failure (logical AND).</summary>
    public static string? Validate(JsonElement claims, IReadOnlyList<RequiredClaim>? requiredClaims)
    {
        if (requiredClaims is null) return null;

        foreach (var required in requiredClaims)
        {
            var error = ValidateOne(claims, required);
            if (error is not null) return error;
        }

        return null;
    }

    private static string? ValidateOne(JsonElement claims, RequiredClaim required)
    {
        if (!TryGetClaim(claims, required.Claim, out var value))
            return $"Missing required claim '{required.Claim}'.";

        // No matcher configured — existence alone satisfies the rule.
        if (required.EqualsValue is null && required.AnyOf is null && required.AllOf is null)
            return null;

        var values = ToStringSet(value, required.Delimiter);

        if (required.EqualsValue is not null)
            return values.Contains(required.EqualsValue)
                ? null
                : $"Claim '{required.Claim}' does not match the required value.";

        if (required.AnyOf is not null)
            return values.Overlaps(required.AnyOf)
                ? null
                : $"Claim '{required.Claim}' does not include any of the required values.";

        // AllOf
        return required.AllOf!.All(values.Contains)
            ? null
            : $"Claim '{required.Claim}' is missing one or more required values.";
    }

    // Literal top-level name first — handles a namespaced claim name that itself contains
    // dots (e.g. Auth0's "https://example.com/roles"). Only falls back to dot-path
    // traversal (e.g. Keycloak's "realm_access.roles") when no literal match exists.
    private static bool TryGetClaim(JsonElement claims, string name, out JsonElement value)
    {
        if (claims.ValueKind == JsonValueKind.Object && claims.TryGetProperty(name, out value))
            return true;

        var current = claims;
        foreach (var segment in name.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                value = default;
                return false;
            }
        }

        value = current;
        return true;
    }

    // A JSON array becomes the set of its members; a single string is one member, unless
    // delimiter is set (splits a space-delimited OAuth scope claim); anything else (bool,
    // number) becomes its invariant string form.
    private static HashSet<string> ToStringSet(JsonElement value, string? delimiter)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                set.Add(ScalarToString(item));
            return set;
        }

        if (value.ValueKind == JsonValueKind.String && delimiter is not null)
        {
            foreach (var part in value.GetString()!.Split(delimiter, StringSplitOptions.RemoveEmptyEntries))
                set.Add(part);
            return set;
        }

        set.Add(ScalarToString(value));
        return set;
    }

    private static string ScalarToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.True   => "true",
        JsonValueKind.False  => "false",
        JsonValueKind.Number => value.GetRawText(),
        _                    => value.GetRawText()
    };
}

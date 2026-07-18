using System.Text.Json;
using Xunit;
using ConduitSharp.Security.Jwt;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class RequiredClaimsValidatorTests
{
    private static JsonElement Claims(string json) => JsonDocument.Parse(json).RootElement;

    // -------------------------------------------------------------------------
    // No rules configured
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_NullRules_ReturnsNull()
    {
        Assert.Null(RequiredClaimsValidator.Validate(Claims("""{"sub":"u1"}"""), null));
    }

    [Fact]
    public void Validate_EmptyRules_ReturnsNull()
    {
        Assert.Null(RequiredClaimsValidator.Validate(Claims("""{"sub":"u1"}"""), []));
    }

    // -------------------------------------------------------------------------
    // Existence-only (no matcher)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_ExistenceOnly_ClaimPresent_ReturnsNull()
    {
        var rules = new[] { new RequiredClaim { Claim = "hd" } };
        Assert.Null(RequiredClaimsValidator.Validate(Claims("""{"hd":"example.com"}"""), rules));
    }

    [Fact]
    public void Validate_ExistenceOnly_ClaimMissing_ReturnsError()
    {
        var rules = new[] { new RequiredClaim { Claim = "hd" } };
        Assert.Equal("Missing required claim 'hd'.",
            RequiredClaimsValidator.Validate(Claims("""{"sub":"u1"}"""), rules));
    }

    // -------------------------------------------------------------------------
    // equals
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_Equals_StringMatch_ReturnsNull()
    {
        var rules = new[] { new RequiredClaim { Claim = "email_verified", EqualsValue = "true" } };
        Assert.Null(RequiredClaimsValidator.Validate(Claims("""{"email_verified":true}"""), rules));
    }

    [Fact]
    public void Validate_Equals_BoolMismatch_ReturnsError()
    {
        var rules = new[] { new RequiredClaim { Claim = "email_verified", EqualsValue = "true" } };
        Assert.Equal("Claim 'email_verified' does not match the required value.",
            RequiredClaimsValidator.Validate(Claims("""{"email_verified":false}"""), rules));
    }

    [Fact]
    public void Validate_Equals_NumberClaim_ComparesRawText()
    {
        var rules = new[] { new RequiredClaim { Claim = "ver", EqualsValue = "2" } };
        Assert.Null(RequiredClaimsValidator.Validate(Claims("""{"ver":2}"""), rules));
    }

    // -------------------------------------------------------------------------
    // anyOf — array claim (Entra app roles)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_AnyOf_ArrayClaimIntersects_ReturnsNull()
    {
        var rules = new[] { new RequiredClaim { Claim = "roles", AnyOf = ["Finance.Reader", "Finance.Admin"] } };
        Assert.Null(RequiredClaimsValidator.Validate(
            Claims("""{"roles":["Other","Finance.Admin"]}"""), rules));
    }

    [Fact]
    public void Validate_AnyOf_ArrayClaimNoOverlap_ReturnsError()
    {
        var rules = new[] { new RequiredClaim { Claim = "roles", AnyOf = ["Finance.Reader"] } };
        Assert.Equal("Claim 'roles' does not include any of the required values.",
            RequiredClaimsValidator.Validate(Claims("""{"roles":["Other"]}"""), rules));
    }

    [Fact]
    public void Validate_AnyOf_SingleStringClaim_TreatsAsOneElementSet()
    {
        var rules = new[] { new RequiredClaim { Claim = "role", AnyOf = ["admin", "owner"] } };
        Assert.Null(RequiredClaimsValidator.Validate(Claims("""{"role":"admin"}"""), rules));
    }

    // -------------------------------------------------------------------------
    // allOf + delimiter — space-delimited scopes (Entra/Okta scp)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_AllOf_DelimitedScopeContainsAll_ReturnsNull()
    {
        var rules = new[]
        {
            new RequiredClaim { Claim = "scp", AllOf = ["reports.read"], Delimiter = " " }
        };
        Assert.Null(RequiredClaimsValidator.Validate(
            Claims("""{"scp":"reports.read reports.write"}"""), rules));
    }

    [Fact]
    public void Validate_AllOf_DelimitedScopeMissingOne_ReturnsError()
    {
        var rules = new[]
        {
            new RequiredClaim { Claim = "scp", AllOf = ["reports.read", "reports.write"], Delimiter = " " }
        };
        Assert.Equal("Claim 'scp' is missing one or more required values.",
            RequiredClaimsValidator.Validate(Claims("""{"scp":"reports.read"}"""), rules));
    }

    [Fact]
    public void Validate_AllOf_NoDelimiter_TreatsWholeStringAsOneElement()
    {
        // Without a delimiter, "reports.read reports.write" is a single opaque value —
        // allOf against two distinct scopes can never match.
        var rules = new[]
        {
            new RequiredClaim { Claim = "scp", AllOf = ["reports.read", "reports.write"] }
        };
        Assert.Equal("Claim 'scp' is missing one or more required values.",
            RequiredClaimsValidator.Validate(Claims("""{"scp":"reports.read reports.write"}"""), rules));
    }

    // -------------------------------------------------------------------------
    // Nested claim path (Keycloak realm_access.roles)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_NestedPath_ResolvesThroughObjectGraph()
    {
        var rules = new[] { new RequiredClaim { Claim = "realm_access.roles", AnyOf = ["finance"] } };
        Assert.Null(RequiredClaimsValidator.Validate(
            Claims("""{"realm_access":{"roles":["finance","other"]}}"""), rules));
    }

    [Fact]
    public void Validate_NestedPath_IntermediateSegmentMissing_ReturnsMissingClaim()
    {
        var rules = new[] { new RequiredClaim { Claim = "realm_access.roles", AnyOf = ["finance"] } };
        Assert.Equal("Missing required claim 'realm_access.roles'.",
            RequiredClaimsValidator.Validate(Claims("""{"sub":"u1"}"""), rules));
    }

    // -------------------------------------------------------------------------
    // Literal-name precedence (Auth0 namespaced claim containing dots)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_LiteralNameWithDots_PrefersLiteralOverPathTraversal()
    {
        // "https://example.com/roles" contains no dots that would form a nested path here,
        // but this proves the literal top-level lookup is tried first regardless.
        var rules = new[] { new RequiredClaim { Claim = "https://example.com/roles", AnyOf = ["admin"] } };
        Assert.Null(RequiredClaimsValidator.Validate(
            Claims("""{"https://example.com/roles":["admin"]}"""), rules));
    }

    [Fact]
    public void Validate_DottedLiteralName_NotShadowedByAccidentalNestedPath()
    {
        // A literal key containing a dot ("a.b") must win over interpreting it as a path
        // into a nested object also named "a" with property "b".
        var rules = new[] { new RequiredClaim { Claim = "a.b", AnyOf = ["literal-wins"] } };
        Assert.Null(RequiredClaimsValidator.Validate(
            Claims("""{"a.b":"literal-wins","a":{"b":"path-value"}}"""), rules));
    }

    // -------------------------------------------------------------------------
    // Multiple rules — logical AND, first failure wins
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_MultipleRules_AllPass_ReturnsNull()
    {
        var rules = new[]
        {
            new RequiredClaim { Claim = "roles", AnyOf = ["Admin"] },
            new RequiredClaim { Claim = "hd" }
        };
        Assert.Null(RequiredClaimsValidator.Validate(
            Claims("""{"roles":["Admin"],"hd":"example.com"}"""), rules));
    }

    [Fact]
    public void Validate_MultipleRules_SecondFails_ReturnsSecondError()
    {
        var rules = new[]
        {
            new RequiredClaim { Claim = "roles", AnyOf = ["Admin"] },
            new RequiredClaim { Claim = "hd" }
        };
        Assert.Equal("Missing required claim 'hd'.",
            RequiredClaimsValidator.Validate(Claims("""{"roles":["Admin"]}"""), rules));
    }
}

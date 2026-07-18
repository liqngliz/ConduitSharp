using Xunit;
using ConduitSharp.Security.Jwt;

namespace ConduitSharp.Security.Tests.Jwt;

public sealed class RequiredClaimTests
{
    // -------------------------------------------------------------------------
    // Claim name
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyClaimName_Throws()
    {
        var rule = new RequiredClaim { Claim = "" };
        Assert.Throws<InvalidOperationException>(rule.Validate);
    }

    [Fact]
    public void Validate_WhitespaceClaimName_Throws()
    {
        var rule = new RequiredClaim { Claim = "   " };
        Assert.Throws<InvalidOperationException>(rule.Validate);
    }

    [Fact]
    public void Validate_ValidClaimName_NoMatcher_DoesNotThrow()
    {
        new RequiredClaim { Claim = "roles" }.Validate();
    }

    // -------------------------------------------------------------------------
    // At most one matcher
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_EqualsAndAnyOfBothSet_Throws()
    {
        var rule = new RequiredClaim { Claim = "roles", EqualsValue = "Admin", AnyOf = ["Admin"] };
        var ex = Assert.Throws<InvalidOperationException>(rule.Validate);
        Assert.Contains("at most one of", ex.Message);
    }

    [Fact]
    public void Validate_AnyOfAndAllOfBothSet_Throws()
    {
        var rule = new RequiredClaim { Claim = "roles", AnyOf = ["Admin"], AllOf = ["Admin"] };
        Assert.Throws<InvalidOperationException>(rule.Validate);
    }

    [Fact]
    public void Validate_AllThreeMatchersSet_Throws()
    {
        var rule = new RequiredClaim
        {
            Claim = "roles", EqualsValue = "Admin", AnyOf = ["Admin"], AllOf = ["Admin"]
        };
        Assert.Throws<InvalidOperationException>(rule.Validate);
    }

    [Fact]
    public void Validate_OnlyEquals_DoesNotThrow()
    {
        new RequiredClaim { Claim = "roles", EqualsValue = "Admin" }.Validate();
    }

    // -------------------------------------------------------------------------
    // Non-empty anyOf/allOf
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyAnyOf_Throws()
    {
        var rule = new RequiredClaim { Claim = "roles", AnyOf = [] };
        Assert.Throws<InvalidOperationException>(rule.Validate);
    }

    [Fact]
    public void Validate_EmptyAllOf_Throws()
    {
        var rule = new RequiredClaim { Claim = "scp", AllOf = [] };
        Assert.Throws<InvalidOperationException>(rule.Validate);
    }

    // -------------------------------------------------------------------------
    // ValidateAll
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateAll_Null_DoesNotThrow()
    {
        RequiredClaim.ValidateAll(null);
    }

    [Fact]
    public void ValidateAll_AllValid_DoesNotThrow()
    {
        RequiredClaim.ValidateAll([
            new RequiredClaim { Claim = "roles", AnyOf = ["Admin"] },
            new RequiredClaim { Claim = "hd" }
        ]);
    }

    [Fact]
    public void ValidateAll_OneInvalid_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => RequiredClaim.ValidateAll([
            new RequiredClaim { Claim = "roles", AnyOf = ["Admin"] },
            new RequiredClaim { Claim = "" }
        ]));
    }
}

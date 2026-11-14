using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Locations;

namespace QuroErp.Domain.Tests.Mm.Locations;

/// <summary>Location master rules (MM-UC-034): code, the system Location '*' and one code per warehouse (MM-BR-712).</summary>
public class LocationRulesTests
{
    private static readonly LocationState Star = new(1, 10, "*", null, true, true, null);
    private static readonly LocationState A01 = new(2, 10, "A-01", "Shelf A1", false, true, null);

    [Theory, Trait("Id", "MM-UC-034-T02"), Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    [InlineData(" a-01 ", "A-01")]
    [InlineData("r1_b2", "R1_B2")]
    [InlineData("ABCDEFGHIJ0123456789", "ABCDEFGHIJ0123456789")]
    public void NormalizeCode_TrimsAndUpperCases(string code, string expected) =>
        LocationRules.NormalizeCode(code).Should().Be(expected);

    [Theory, Trait("Id", "MM-UC-034-T03"), Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("ABCDEFGHIJ0123456789X")]
    [InlineData("A 01")]
    [InlineData("A.01")]
    [InlineData("*")]
    [InlineData("선반")]
    public void NormalizeCode_RejectsAnEmptyLongOrInvalidCode_AndTheSystemCode(string? code)
    {
        var act = () => LocationRules.NormalizeCode(code);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(LocationRules.InvalidCode);
    }

    [Fact, Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    public void Normalize_StoresEmptyNameAndRemarksAsNull_AndTrimsThem()
    {
        LocationRules.Normalize(A01 with { LocationName = " ", Remarks = " note " }).Should().Be(A01 with { LocationName = null, Remarks = "note" });
    }

    [Fact, Trait("Id", "MM-UC-034-T04"), Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    public void TheSystemLocation_CannotBeChangedOrDeleted()
    {
        var change = () => LocationRules.EnsureChangeable(Star, null);
        var delete = () => LocationRules.EnsureDeletable(Star);

        change.Should().Throw<DomainException>().Which.Args.Should().Equal("*");
        delete.Should().Throw<DomainException>().Which.Code.Should().Be(LocationRules.InvalidCode);
    }

    [Theory, Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-01")]
    public void EnsureChangeable_AllowsTheStoredCodeOrNone(string? code)
    {
        var act = () => LocationRules.EnsureChangeable(A01, code);

        act.Should().NotThrow();
        LocationRules.EnsureDeletable(A01);
    }

    [Fact, Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    public void EnsureChangeable_RejectsAChangedCode()
    {
        var act = () => LocationRules.EnsureChangeable(A01, "A-02");

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("A-02");
    }

    [Fact, Trait("Id", "MM-UC-034-T03"), Trait("Id", "MM-UT-712"), Trait("Rule", "MM-BR-712")]
    public void EnsureUnique_RejectsACodeTwiceInOneWarehouse_ButNotInTwoWarehouses()
    {
        var twice = () => LocationRules.EnsureUnique([Star, A01, A01 with { Id = 0 }]);
        var otherWarehouse = () => LocationRules.EnsureUnique([Star, A01, A01 with { Id = 0, WarehouseId = 11 }, Star with { Id = 3, WarehouseId = 11 }]);

        twice.Should().Throw<DomainException>().Which.Args.Should().Equal("A-01");
        otherWarehouse.Should().NotThrow();
    }

    [Fact, Trait("Id", "MM-UT-717"), Trait("Rule", "MM-BR-717")]
    public void BlockReasons_AreTheMessageArgs() =>
        new[] { LocationBlockReasons.OnHand, LocationBlockReasons.HasMovement }.Should().Equal("ONHAND", "HAS_MOVEMENT");
}

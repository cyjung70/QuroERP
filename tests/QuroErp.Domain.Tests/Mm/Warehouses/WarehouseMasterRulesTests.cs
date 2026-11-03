using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Warehouses;

namespace QuroErp.Domain.Tests.Mm.Warehouses;

/// <summary>Warehouse master rules (MM-UC-017): code and name (MM-BR-701), default warehouse (MM-BR-702), consignment (MM-BR-709).</summary>
public class WarehouseMasterRulesTests
{
    private static readonly WarehouseState Main = new(1, "MAIN", "Main", true, true, WarehouseTypes.Normal, null);
    private static readonly WarehouseState Second = new(2, "W02", "Second", false, true, WarehouseTypes.Normal, null);
    private static readonly WarehouseState Consignment = new(3, "CONSIGN", "Consignment", false, true, WarehouseTypes.Consignment, null);

    [Theory, Trait("Id", "MM-UC-017-T03"), Trait("Id", "MM-UT-701"), Trait("Rule", "MM-BR-701")]
    [InlineData(" w-01 ", "W-01")]
    [InlineData("a_b", "A_B")]
    [InlineData("ABCDEFGHIJ", "ABCDEFGHIJ")]
    public void NormalizeCode_TrimsAndUpperCases(string code, string expected) =>
        WarehouseRules.NormalizeCode(code).Should().Be(expected);

    [Theory, Trait("Id", "MM-UC-017-T03"), Trait("Id", "MM-UT-701"), Trait("Rule", "MM-BR-701")]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("W 01")]
    [InlineData("창고")]
    public void NormalizeCode_RejectsAnEmptyLongOrInvalidCode(string? code)
    {
        var act = () => WarehouseRules.NormalizeCode(code);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(WarehouseRules.InvalidCode);
    }

    [Fact, Trait("Id", "MM-UT-701"), Trait("Rule", "MM-BR-701")]
    public void Normalize_RequiresAName_AndStoresEmptyRemarksAsNull()
    {
        WarehouseRules.Normalize(Second with { WarehouseName = " Second ", Remarks = " " })
            .Should().Be(Second with { WarehouseName = "Second", Remarks = null });

        var act = () => WarehouseRules.Normalize(Second with { WarehouseName = " " });

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(WarehouseRules.InvalidCode);
        error.Args.Should().Equal("W02");
    }

    [Fact, Trait("Id", "MM-UC-017-T03"), Trait("Id", "MM-UT-701"), Trait("Rule", "MM-BR-701")]
    public void EnsureKeysUnchanged_RejectsAnotherCode_ButAllowsTheSameCodeInAnyCase()
    {
        WarehouseRules.EnsureKeysUnchanged(Second, "w02", null);
        WarehouseRules.EnsureKeysUnchanged(Second, null, null);

        var act = () => WarehouseRules.EnsureKeysUnchanged(Second, "W03", null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(WarehouseRules.InvalidCode);
    }

    [Fact, Trait("Id", "MM-UC-017-T02"), Trait("Id", "MM-UT-701"), Trait("Rule", "MM-BR-701")]
    public void EnsureFinalState_RejectsADuplicateCode_IgnoringCase()
    {
        var act = () => WarehouseRules.EnsureFinalState([Main, Second, Second with { Id = 0, WarehouseCode = "w02" }]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(WarehouseRules.InvalidCode);
    }

    [Fact, Trait("Id", "MM-UC-017-T04"), Trait("Id", "MM-UT-702"), Trait("Rule", "MM-BR-702")]
    public void EnsureFinalState_NeedsExactlyOneDefaultWarehouse_InUse()
    {
        WarehouseRules.EnsureFinalState([Main, Second, Consignment]);

        var none = () => WarehouseRules.EnsureFinalState([Main with { IsDefault = false }, Second]);
        var two = () => WarehouseRules.EnsureFinalState([Main, Second with { IsDefault = true }]);
        var inactive = () => WarehouseRules.EnsureFinalState([Main with { UseYn = false }, Second]);

        none.Should().Throw<DomainException>().Which.Code.Should().Be(WarehouseRules.DefaultCode);
        two.Should().Throw<DomainException>().Which.Args.Should().Equal("MAIN, W02");
        inactive.Should().Throw<DomainException>().Which.Code.Should().Be(WarehouseRules.DefaultCode);
    }

    [Fact, Trait("Id", "MM-UT-702"), Trait("Rule", "MM-BR-702"), Trait("Rule", "MM-BR-709")]
    public void EnsureDeletable_RejectsTheDefaultAndTheConsignmentWarehouse()
    {
        WarehouseRules.EnsureDeletable(Second);

        var main = () => WarehouseRules.EnsureDeletable(Main);
        var consignment = () => WarehouseRules.EnsureDeletable(Consignment);

        main.Should().Throw<DomainException>().Which.Code.Should().Be(WarehouseRules.DefaultCode);
        consignment.Should().Throw<DomainException>().Which.Code.Should().Be(WarehouseRules.ConsignmentCode);
    }

    [Fact, Trait("Id", "MM-UT-709"), Trait("Rule", "MM-BR-709")]
    public void TheConsignmentWarehouse_CannotBeTheDefault_NotInUse_OrAdded_AndNoTypeChanges()
    {
        var asDefault = () => WarehouseRules.EnsureFinalState([Main with { IsDefault = false }, Consignment with { IsDefault = true }]);
        var inactive = () => WarehouseRules.EnsureFinalState([Main, Consignment with { UseYn = false }]);
        var added = () => WarehouseRules.NewWarehouseType("CON2", "consignment");
        var changed = () => WarehouseRules.EnsureKeysUnchanged(Second, null, WarehouseTypes.Consignment);

        foreach (var act in new Action[] { () => asDefault(), inactive, () => added(), changed })
        {
            act.Should().Throw<DomainException>().Which.Code.Should().Be(WarehouseRules.ConsignmentCode);
        }
        WarehouseRules.NewWarehouseType("W03", null).Should().Be(WarehouseTypes.Normal);
        WarehouseRules.NewWarehouseType("W03", "normal").Should().Be(WarehouseTypes.Normal);
        WarehouseRules.EnsureKeysUnchanged(Consignment, "CONSIGN", "CONSIGNMENT");
    }
}

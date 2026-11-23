using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>PDA move (MM-UC-039): the type it saves (MM-BR-922) and the places it scans (MM-BR-923).</summary>
public class PdaMoveRulesTests
{
    [Theory, Trait("Id", "MM-UT-922"), Trait("Id", "MM-UC-039-T05"), Trait("Rule", "MM-BR-922")]
    [InlineData(1L, null, "LT")]
    [InlineData(1L, 1L, "LT")]
    [InlineData(1L, 2L, "TR")]
    public void TypeFor_IsLtInsideTheSendingWarehouse_AndTrToAnother(long from, long? to, string expected) =>
        StockTransferRules.TypeFor(from, to).Should().Be(expected);

    [Theory, Trait("Id", "MM-UT-922"), Trait("Rule", "MM-BR-922")]
    [InlineData("FROM", new[] { "LC", "PL" })]
    [InlineData(" from ", new[] { "LC", "PL" })]
    [InlineData("TO", new[] { "LC" })]
    public void KindsOf_FromTakesLocationsAndPallets_ToTakesLocations(string step, string[] kinds) =>
        PdaMoveRules.KindsOf(step).Should().Equal(kinds);

    [Theory, Trait("Id", "MM-UT-922"), Trait("Rule", "MM-BR-922")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ITEM")]
    public void KindsOf_AnotherStep_IsCommonE002(string? step)
    {
        var act = () => PdaMoveRules.KindsOf(step);

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args[0]).Should().Be((PdaMoveRules.InvalidStepCode, (object)"step"));
    }

    [Theory, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923")]
    [InlineData(true, true, true, null)]
    [InlineData(true, true, true, "ACTIVE")]
    public void EnsurePlace_AFoundPlaceInUse_Passes(bool found, bool warehouseInUse, bool locationInUse, string? palletStatus)
    {
        var act = () => PdaMoveRules.EnsurePlace(found, warehouseInUse, locationInUse, palletStatus, "LC|MAIN|A1");

        act.Should().NotThrow();
    }

    [Theory, Trait("Id", "MM-UT-923"), Trait("Rule", "MM-BR-923"), Trait("Rule", "MM-BR-912")]
    [InlineData(false, false, false, null)]
    [InlineData(true, false, true, null)]
    [InlineData(true, true, false, null)]
    [InlineData(true, true, true, "CLOSED")]
    public void EnsurePlace_AMissingPlace_OneNotInUse_OrAClosedPallet_IsMmE923(
        bool found, bool warehouseInUse, bool locationInUse, string? palletStatus)
    {
        var act = () => PdaMoveRules.EnsurePlace(found, warehouseInUse, locationInUse, palletStatus, "LC|MAIN|A1");

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args.Single()).Should().Be((LabelCode.UnknownCode, (object)"LC|MAIN|A1"));
    }
}

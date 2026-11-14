using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>Stock transfer types TR and LT (MM-BR-715) and the new movement types (inventory.md, MM-BR-606).</summary>
public class StockTransferRulesTests
{
    [Theory, Trait("Id", "MM-UT-715"), Trait("Rule", "MM-BR-715")]
    [InlineData("TR", 1, 2, 10, 10)]
    [InlineData("TR", 1, 2, 10, 20)]
    [InlineData("LT", 1, 1, 10, 11)]
    public void EnsureLine_ValidTransfer_Passes(string type, long from, long to, long fromLocation, long toLocation)
    {
        FluentActions.Invoking(() => StockTransferRules.EnsureLine(type, 1, from, to, fromLocation, toLocation)).Should().NotThrow();
    }

    [Theory, Trait("Id", "MM-UT-715"), Trait("Rule", "MM-BR-715")]
    [InlineData("LT", 1, 1, 10, 10)]
    [InlineData("LT", 1, 2, 10, 20)]
    [InlineData("TR", 1, 1, 10, 11)]
    [InlineData("GR", 1, 2, 10, 20)]
    [InlineData(null, 1, 2, 10, 20)]
    public void EnsureLine_SameLocationLtAnotherWarehouseLtSameWarehouseTrOrOtherType_IsMmE715(
        string? type, long from, long to, long fromLocation, long toLocation)
    {
        var error = FluentActions.Invoking(() => StockTransferRules.EnsureLine(type, 3, from, to, fromLocation, toLocation))
            .Should().Throw<DomainException>().Which;

        error.Code.Should().Be(StockTransferRules.InvalidCode);
        error.Args.Should().Equal(type ?? "", 3);
    }

    [Theory, Trait("Id", "MM-UT-606"), Trait("Rule", "MM-BR-606")]
    [InlineData("LT", 0, false)]
    [InlineData("BI", -1, false)]
    [InlineData("DA", -1, false)]
    [InlineData("DR", 1, true)]
    public void MovementType_NewTypes_HaveTheirSignAndCostRule(string type, int sign, bool recalculates)
    {
        MovementType.IsKnown(type).Should().BeTrue();
        MovementType.OriginalSign(type).Should().Be(sign);
        MovementType.RecalculatesCost(type, 1).Should().Be(recalculates);
    }
}

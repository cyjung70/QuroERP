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

    [Theory, Trait("Id", "MM-UT-705"), Trait("Rule", "MM-BR-705")]
    [InlineData("TR", 1, 2, 5, true)]
    [InlineData("LT", 1, 1, 5, true)]
    public void EnsureLineValues_TwoWarehousesPositiveQtyStockItem_Passes(string type, long from, long to, decimal qty, bool holdsStock)
    {
        FluentActions.Invoking(() => StockTransferRules.EnsureLineValues(type, 1, "BOLT", qty, holdsStock, from, to)).Should().NotThrow();
    }

    [Theory, Trait("Id", "MM-UT-705"), Trait("Rule", "MM-BR-705"), Trait("Rule", "MM-BR-008")]
    [InlineData("TR", 1, 1, 5, true)]
    [InlineData("TR", 1, 2, 0, true)]
    [InlineData("TR", 1, 2, -3, true)]
    [InlineData("TR", 1, 2, 5, false)]
    [InlineData("LT", 1, 1, 0, true)]
    public void EnsureLineValues_SameWarehouseTrNoQtyOrNoStockItem_IsMmE705(string type, long from, long to, decimal qty, bool holdsStock)
    {
        var error = FluentActions.Invoking(() => StockTransferRules.EnsureLineValues(type, 2, "BOLT", qty, holdsStock, from, to))
            .Should().Throw<DomainException>().Which;

        (error.Code, error.Args).Should().BeEquivalentTo((StockTransferRules.LineCode, new object[] { 2, "BOLT" }));
    }

    [Theory, Trait("Id", "MM-UT-706"), Trait("Rule", "MM-BR-706")]
    [InlineData(3, 12.3456, 2, 37.04)]
    [InlineData(3, 12.335, 2, 37.01)]
    [InlineData(7, 1.5, 0, 11)]
    [InlineData(2, 0.125, 2, 0.25)]
    public void Amount_IsQtyTimesCost_RoundedHalfUpToTheBaseCurrency(decimal qty, decimal price, int decimals, decimal expected)
    {
        StockTransferRules.Amount(qty, price, decimals).Should().Be(expected);
    }

    [Fact, Trait("Id", "MM-UT-912"), Trait("Rule", "MM-BR-912")]
    public void EnsurePallet_WholeActivePalletOfTheLine_Passes()
    {
        FluentActions.Invoking(() => StockTransferRules.EnsurePallet(1, 9, Pallet(), 11, 40m, 1, 30, 501)).Should().NotThrow();
        FluentActions.Invoking(() => StockTransferRules.EnsurePallet(1, 9, Pallet(), 11, 40m, 1, null, null)).Should().NotThrow();
    }

    public static TheoryData<PalletState?, long, decimal, long, long?, long?, object> WrongPallets => new()
    {
        { null, 11, 40m, 1, null, null, 9L },
        { Pallet() with { Status = PalletRules.Closed }, 11, 40m, 1, null, null, "PL1" },
        { Pallet(), 12, 40m, 1, null, null, "PL1" },
        { Pallet(), 11, 39m, 1, null, null, "PL1" },
        { Pallet(), 11, 40m, 2, null, null, "PL1" },
        { Pallet(), 11, 40m, 1, 31L, null, "PL1" },
        { Pallet(), 11, 40m, 1, null, 502L, "PL1" },
    };

    [Theory, Trait("Id", "MM-UT-912"), Trait("Rule", "MM-BR-912")]
    [MemberData(nameof(WrongPallets))]
    public void EnsurePallet_MissingClosedOtherItemPartQtyOrOtherKey_IsMmE912(
        PalletState? pallet, long itemId, decimal qty, long warehouseId, long? locationId, long? lotId, object shown)
    {
        var error = FluentActions.Invoking(() => StockTransferRules.EnsurePallet(3, 9, pallet, itemId, qty, warehouseId, locationId, lotId))
            .Should().Throw<DomainException>().Which;

        (error.Code, error.Args).Should().BeEquivalentTo((StockTransferRules.PalletCode, new[] { 3, shown }));
    }

    [Fact, Trait("Id", "MM-UT-912"), Trait("Rule", "MM-BR-912")]
    public void EnsurePalletsOnce_APalletOnTwoLines_IsMmE912OnTheSecond()
    {
        FluentActions.Invoking(() => StockTransferRules.EnsurePalletsOnce([(1, 9L), (2, null), (3, 8L)])).Should().NotThrow();
        var error = FluentActions.Invoking(() => StockTransferRules.EnsurePalletsOnce([(1, 9L), (2, null), (3, 9L)]))
            .Should().Throw<DomainException>().Which;

        (error.Code, error.Args).Should().BeEquivalentTo((StockTransferRules.PalletCode, new object[] { 3, 9L }));
    }

    [Theory, Trait("Id", "MM-UT-715"), Trait("Rule", "MM-BR-715")]
    [InlineData("TR", true)]
    [InlineData("LT", true)]
    [InlineData("GI", false)]
    [InlineData(null, false)]
    public void IsType_OnlyTrAndLt(string? type, bool expected) => StockTransferRules.IsType(type).Should().Be(expected);

    private static PalletState Pallet() => new(9, "PL1", 11, 501, 1, 30, 40m, PalletRules.Active);
}

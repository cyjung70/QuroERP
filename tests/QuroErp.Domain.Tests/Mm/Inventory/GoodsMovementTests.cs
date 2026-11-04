using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>Ledger rows: sign by movement type and reversals (MM-BR-601, MM-BR-606).</summary>
public class GoodsMovementTests
{
    private static readonly DateOnly Day = new(2026, 10, 10);

    [Theory, Trait("Id", "MM-UT-606")]
    [InlineData("GR", 1)]
    [InlineData("RT", 1)]
    [InlineData("BP", 1)]
    [InlineData("UR", 1)]
    [InlineData("GI", -1)]
    [InlineData("DN", -1)]
    [InlineData("CT", 1)]
    [InlineData("CT", -1)]
    [InlineData("TR", 1)]
    [InlineData("TR", -1)]
    [InlineData("VR", -1)]
    public void Create_SignAllowedForType_KeepsQtyAndAmountSign(string type, int sign)
    {
        var movement = GoodsMovement.Create(type, Day, itemId: 1, warehouseId: 2, qty: 4 * sign, prc: 2.5m);

        movement.Qty.Should().Be(4 * sign);
        movement.Amount.Should().Be(10 * sign);
        movement.ParentMovementId.Should().BeNull();
    }

    [Theory, Trait("Id", "MM-UT-606")]
    [InlineData("GR", -1)]
    [InlineData("UR", -1)]
    [InlineData("GI", 1)]
    [InlineData("DN", 1)]
    [InlineData("GR", 0)]
    [InlineData("CT", 0)]
    [InlineData("VR", 1)]
    [InlineData("VR", 0)]
    [InlineData("XX", 1)]
    public void Create_ZeroQtyWrongSignOrUnknownType_ThrowsMmE606(string type, int qty)
    {
        var act = () => GoodsMovement.Create(type, Day, 1, 2, qty, 1m);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-606");
    }

    [Fact, Trait("Id", "MM-UT-601")]
    public void Reverse_MakesSameTypeOppositeSignOnReversalDatePointingToOriginal()
    {
        var original = GoodsMovement.Restore(10, "GM2026100000000001", "GR", Day, 1, 2, 4, 2.5m, "GR-001", 3, null, false);

        var reversal = original.Reverse(new DateOnly(2026, 10, 20), alreadyReversed: false);

        reversal.MovType.Should().Be("GR");
        reversal.DocumentDt.Should().Be(new DateOnly(2026, 10, 20));
        reversal.Qty.Should().Be(-4);
        reversal.Prc.Should().Be(2.5m);
        reversal.Amount.Should().Be(-10);
        reversal.ParentMovementId.Should().Be(10);
        (reversal.ItemId, reversal.WarehouseId, reversal.RefKey, reversal.RefSeq).Should().Be((1L, 2L, "GR-001", 3));
        reversal.DocumentNo.Should().BeEmpty("a reversal gets a new number when saved");
    }

    [Theory, Trait("Id", "MM-UT-606"), Trait("Rule", "MM-BR-606")]
    [InlineData(-12.5)]
    [InlineData(30)]
    public void Create_PriceVariance_HasQtyZero_AndOnlyTheAmount(double amount)
    {
        var movement = GoodsMovement.Create("PV", Day, 1, 2, qty: 0, prc: 99m, amount: (decimal)amount);

        (movement.Qty, movement.Prc, movement.Amount).Should().Be((0m, 0m, (decimal)amount));
    }

    [Theory, Trait("Id", "MM-UT-606"), Trait("Rule", "MM-BR-606")]
    [InlineData(1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, null)]
    public void Create_PriceVarianceWithQtyOrWithoutAmount_ThrowsMmE606(int qty, int? amount)
    {
        var act = () => GoodsMovement.Create("PV", Day, 1, 2, qty, 1m, amount: amount);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-606");
    }

    [Fact, Trait("Id", "MM-UT-601"), Trait("Rule", "MM-BR-601")]
    public void Reverse_PriceVariance_NegatesTheAmount()
    {
        var original = GoodsMovement.Restore(10, "GM2026100000000001", "PV", Day, 1, 2, 0m, 0m, "SI-1", 1, null, false, amount: 40m);

        var reversal = original.Reverse(Day, alreadyReversed: false);

        (reversal.MovType, reversal.Qty, reversal.Amount, reversal.ParentMovementId).Should().Be(("PV", 0m, -40m, (long?)10));
    }

    [Fact, Trait("Id", "MM-UT-601"), Trait("Rule", "MM-BR-601")]
    public void Reverse_VendorReturn_AddsTheStockBackAtTheSamePrice()
    {
        var original = GoodsMovement.Restore(10, "GM2026100000000001", "VR", Day, 1, 2, -3m, 4m, "VR-1", 1, null, false);

        var reversal = original.Reverse(Day, alreadyReversed: false);

        (reversal.Qty, reversal.Prc, reversal.Amount).Should().Be((3m, 4m, 12m));
    }

    [Fact, Trait("Id", "MM-UT-606")]
    public void Reverse_AlreadyReversed_ThrowsMmE606()
    {
        var original = GoodsMovement.Restore(10, "GM2026100000000001", "GI", Day, 1, 2, -4, 2.5m, null, null, null, false);

        var act = () => original.Reverse(Day, alreadyReversed: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-606");
    }

    [Fact, Trait("Id", "MM-UT-606")]
    public void Reverse_AReversal_ThrowsMmE606()
    {
        var reversal = GoodsMovement.Restore(11, "GM2026100000000002", "GI", Day, 1, 2, 4, 2.5m, null, null, 10, false);

        var act = () => reversal.Reverse(Day, alreadyReversed: false);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MM-E-606");
    }
}

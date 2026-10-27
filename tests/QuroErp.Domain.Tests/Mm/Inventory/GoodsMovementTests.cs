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

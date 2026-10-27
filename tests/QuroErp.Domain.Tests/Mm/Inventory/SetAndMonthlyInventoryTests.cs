using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Tests.Mm.Inventory;

/// <summary>Set expansion (MM-BR-007), monthly roll-up (MM-BR-006) and movement numbers.</summary>
public class SetAndMonthlyInventoryTests
{
    [Theory, Trait("Id", "MM-UT-007")]
    [InlineData(-3, -6, -3)]
    [InlineData(2, 4, 2)]
    public void Expand_EachComponentMovesQtyTimesComponentQtyKeepingSign(decimal qty, decimal first, decimal second)
    {
        var components = new[] { new SetComponent(9, 1, 2), new SetComponent(9, 2, 1) };

        SetComponent.Expand(qty, components).Should().Equal((1L, first), (2L, second));
    }

    [Fact, Trait("Id", "MM-UT-006")]
    public void Roll_SumsReceiptsAndIssuesAndClosesFromOpening()
    {
        var month = MonthlyInventory.Roll(10, 100, [
            ("GR", 5, 60),
            ("GR", -2, -24),   // receipt reversal lowers receipts
            ("DN", -4, -44),
            ("DN", 1, 11),     // issue reversal lowers issues
            ("CT", -1, -11),
            ("TR", 3, 33),
        ]);

        month.Should().Be(new MonthlyInventory(10, 100, RcptQty: 6, RcptAmt: 69, IssueQty: 4, IssueAmt: 44));
        (month.BalQty, month.BalAmt).Should().Be((12m, 125m));
    }

    [Fact, Trait("Id", "MM-UT-006")]
    public void Roll_MonthWithoutMovements_CarriesBalanceForward()
    {
        var month = MonthlyInventory.Roll(7, 70, []);

        (month.BasQty, month.BalQty, month.BalAmt).Should().Be((7m, 7m, 70m));
    }

    [Theory, Trait("Id", "MM-GM-T01")]
    [InlineData(null, "GM2026100000000001")]
    [InlineData("GM2026100000000041", "GM2026100000000042")]
    [InlineData("GM2026090000000041", "GM2026100000000001")]
    [InlineData("OLD-7", "GM2026100000000001")]
    public void DocumentNumber_Next_RestartsEachMonth(string? last, string expected)
    {
        DocumentNumber.Next("GM", new DateOnly(2026, 10, 27), last, 10).Should().Be(expected);
    }
}

using FluentAssertions;
using QuroErp.Domain.Mm.PurchaseOrders;

namespace QuroErp.Domain.Tests.Mm.PurchaseOrders;

/// <summary>Line amounts and totals before tax (MM-BR-015, MM-BR-025). USD amounts have 2 decimals, KRW 0 (BC-BR-421).</summary>
public class PoCalculatorTests
{
    private const int Usd = 2;
    private const int Krw = 0;

    private static PoLine Line(decimal qty, decimal price, decimal discount = 0, bool markedForDelete = false) =>
        new(1, qty, price, discount, MarkedForDelete: markedForDelete);

    [Fact, Trait("Id", "MM-UT-015"), Trait("Rule", "MM-BR-015")]
    public void CalcLine_QtyTimesPrice_RoundsToAmountDecimals()
    {
        var amounts = PoCalculator.CalcLine(Line(3m, 1.3333m), Usd);

        amounts.Gross.Should().Be(4.00m); // 3.9999 → 4.00
        amounts.Net.Should().Be(4.00m);
    }

    [Fact, Trait("Id", "MM-UT-025"), Trait("Rule", "MM-BR-025")]
    public void CalcLine_Discount_RoundsHalfUpAndNetIsGrossMinusDiscount()
    {
        var amounts = PoCalculator.CalcLine(Line(5m, 280.5m, discount: 5m), Usd);

        amounts.Gross.Should().Be(1402.50m);
        amounts.Discount.Should().Be(70.13m); // 70.125 → 70.13
        amounts.Net.Should().Be(1332.37m);
    }

    [Fact, Trait("Id", "MM-UC-003-T07"), Trait("Rule", "MM-BR-025")]
    public void CalcLine_TenPercentDiscount_GrossThousandGivesNetNineHundred()
    {
        var amounts = PoCalculator.CalcLine(Line(10m, 100m, discount: 10m), Krw);

        amounts.Should().Be(new PoLineAmounts(1000m, 100m, 900m));
    }

    [Fact, Trait("Id", "MM-UC-003-T12"), Trait("Rule", "MM-BR-015")]
    public void CalcLine_Krw_HasNoDecimals()
    {
        var amounts = PoCalculator.CalcLine(Line(3m, 333.3333m, discount: 0.5m), Krw);

        amounts.Gross.Should().Be(1000m); // 999.9999 → 1000
        amounts.Discount.Should().Be(5m);
        amounts.Net.Should().Be(995m);
    }

    [Fact, Trait("Id", "MM-UT-015"), Trait("Rule", "MM-BR-015")]
    public void CalcTotals_SumsRoundedLines_LeavesOutLinesMarkedForDelete()
    {
        var lines = new[]
        {
            Line(10m, 1250m),
            Line(5m, 280.5m, discount: 5m),
            Line(20m, 45m, markedForDelete: true),
        };

        var totals = PoCalculator.CalcTotals(lines, Usd);

        totals.Should().Be(new PoTotals(Subtotal: 13902.50m, Discount: 70.13m, Net: 13832.37m));
    }

    [Fact, Trait("Id", "MM-UT-015"), Trait("Rule", "MM-BR-015")]
    public void CalcTotals_NoLines_IsZero()
    {
        PoCalculator.CalcTotals([], Usd).Should().Be(new PoTotals(0m, 0m, 0m));
    }
}

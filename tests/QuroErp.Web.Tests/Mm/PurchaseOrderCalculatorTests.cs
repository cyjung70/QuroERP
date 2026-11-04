using FluentAssertions;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Web.Components.Pages.Mm.Models;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Display math of the purchase order screen (TASK-MM-006): rounding HALF_UP, USD amount 2 / price 4, KRW amount 0.</summary>
public class PurchaseOrderCalculatorTests
{
    private const string HalfUp = PurchaseOrderCalculator.HalfUp;

    [Fact, Trait("Id", "MM-UT-015"), Trait("Rule", "MM-BR-015")]
    public void Line_UsdPrice4_RoundsTheGrossToAmountDecimals()
    {
        PurchaseOrderCalculator.Line(3m, 1.3333m, 0m, 2, HalfUp).Gross.Should().Be(4.00m);
    }

    [Fact, Trait("Id", "MM-UT-025"), Trait("Rule", "MM-BR-025")]
    public void Line_WithDiscount_GivesGrossDiscountAmount_AndLineTax()
    {
        var line = PurchaseOrderCalculator.Line(5m, 280.5m, 5m, 2, HalfUp);

        line.Should().Be(new PoLineAmounts(1402.50m, 70.13m, 1332.37m));
        PurchaseOrderCalculator.Tax(line.Amount, 10m, false, 2, HalfUp).Should().Be(133.24m);
    }

    [Theory, Trait("Id", "MM-UT-032"), Trait("Rule", "MM-BR-032"), Trait("Rule", "BC-BR-543")]
    [InlineData(PurchaseOrderCalculator.LineMode)]
    [InlineData(PurchaseOrderCalculator.DocumentMode)]
    public void Totals_LeaveOutTheDeletedLine_AndGiveTheSameTaxInBothModes(string mode)
    {
        // The line 20 × 45 is marked for delete, so the screen leaves it out before the totals
        PoLineInput[] kept = [new(10m, 1250m, 0m, 1, 10m), new(5m, 280.5m, 5m, 1, 10m)];

        var totals = PurchaseOrderCalculator.Totals(kept, 2, mode, HalfUp);

        totals.Subtotal.Should().Be(13902.50m);
        totals.Discount.Should().Be(70.13m);
        totals.Tax.Should().Be(1383.24m);
        totals.Total.Should().Be(15215.61m);
    }

    [Fact, Trait("Id", "BC-UT-413"), Trait("Rule", "BC-BR-413")]
    public void BaseTotal_IsTotalTimesRate_RoundedToBaseDecimals()
    {
        PurchaseOrderCalculator.BaseTotal(15215.61m, 1382.5m, 0, HalfUp).Should().Be(21035581m);
    }

    [Fact, Trait("Id", "BC-UT-421"), Trait("Rule", "BC-BR-421")]
    public void Krw_AmountDecimals0_RoundsAmountAndTax()
    {
        var totals = PurchaseOrderCalculator.Totals([new PoLineInput(3m, 333.3333m, 0m, 1, 10m)], 0, PurchaseOrderCalculator.LineMode, HalfUp);

        totals.Subtotal.Should().Be(1000m);
        totals.Tax.Should().Be(100m);
    }

    [Fact, Trait("Id", "BC-UT-545"), Trait("Rule", "BC-BR-545")]
    public void DownMode_CutsTheTax()
    {
        PurchaseOrderCalculator.Tax(1009m, 10m, false, 0, PurchaseOrderCalculator.Down).Should().Be(100m);
    }

    [Fact, Trait("Id", "MM-UC-003-T12")]
    public void Decimals_FollowTheDocumentCurrency_AndItsCountry()
    {
        var localization = new LocalizationDto("KR", "KRW", "ko", "Asia/Seoul", "VAT", "yyyy-MM-dd", "LINE", [],
            [new CurrencyDto("KRW", "Won", 0, "KR"), new CurrencyDto("USD", "US Dollar", 2, "US")],
            [new CountryDecimalDto("KR", "MM", 0), new CountryDecimalDto("US", "MM", 4)],
            [new DecimalSettingDto("MM", 2, HalfUp)]);

        var usd = PurchaseOrderDecimals.Of(localization, "USD");
        var krw = PurchaseOrderDecimals.Of(localization, null);

        (usd.Amount, usd.Price, usd.Qty, usd.IsForeign, usd.BaseAmount).Should().Be((2, 4, 2, true, 0));
        (krw.Amount, krw.Price, krw.IsForeign).Should().Be((0, 0, false));
        usd.ColumnFormats["poPrc"].Should().Be("N4");
        krw.ColumnFormats["poAmt"].Should().Be("N0");
    }
}

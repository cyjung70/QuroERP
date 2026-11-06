using FluentAssertions;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Web.Components.Pages.Sd.Models;

namespace QuroErp.Web.Tests.Sd;

/// <summary>Display math of the sales order screen; it mirrors the Domain (SD-BR-005, SD-BR-032, SD-BR-034, BC-BR-543).</summary>
public class SalesOrderCalculatorTests
{
    [Theory, Trait("Id", "SD-UT-005"), Trait("Rule", "SD-BR-005")]
    [InlineData(10, 100, -10, 2, 1000, 100, 900)]
    [InlineData(3, 33.33, -33, 2, 99.99, 33.00, 66.99)]
    [InlineData(1, 9.5, 0, 0, 10, 0, 10)]
    public void Line_RoundsQtyTimesPriceTimesOnePlusTheRate(
        decimal qty, decimal price, decimal rate, int decimals, decimal gross, decimal discount, decimal amount)
    {
        SalesOrderCalculator.Line(qty, price, rate, decimals, "HALF_UP").Should().Be(new SoLineAmounts(gross, discount, amount));
    }

    [Fact, Trait("Id", "SD-UT-032"), Trait("Rule", "SD-BR-032"), Trait("Rule", "BC-BR-543")]
    public void Totals_LineTaxesEachLine_DocumentTaxesEachCodeOnce()
    {
        SoLineInput[] lines = [new(1, 0.05m, 0, 1, 10m), new(1, 0.05m, 0, 1, 10m)];

        SalesOrderCalculator.Totals(lines, 2, "LINE", "HALF_UP").Tax.Should().Be(0.02m);
        SalesOrderCalculator.Totals(lines, 2, "DOCUMENT", "HALF_UP").Tax.Should().Be(0.01m);
    }

    [Fact, Trait("Id", "SD-UT-034"), Trait("Rule", "SD-BR-034"), Trait("Rule", "BC-BR-544")]
    public void Totals_TotalIsSupplyPlusTax_AndAnIncludedPriceHoldsItsTax()
    {
        var totals = SalesOrderCalculator.Totals([new(1, 110m, 0, 3, 10m, PriceIncluded: true), new(2, 50m, -10m, 1, 10m)], 2, "LINE", "HALF_UP");

        totals.Should().Be(new SoDisplayTotals(210m, 10m, 190m, 19m, 209m));
    }

    [Fact, Trait("Id", "BC-UT-421"), Trait("Rule", "BC-BR-421")]
    public void Decimals_TakeTheSdSettings_AndTheDocumentCurrency()
    {
        var localization = new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "LINE", [],
            [new CurrencyDto("USD", "US Dollar", 2, "US"), new CurrencyDto("KRW", "Won", 0, "KR")],
            [new CountryDecimalDto("US", "SD", 3), new CountryDecimalDto("US", "MM", 4)],
            [new DecimalSettingDto("SD", 1, "DOWN"), new DecimalSettingDto("MM", 2, "HALF_UP")]);

        var decimals = SalesOrderDecimals.Of(localization, "KRW");

        (decimals.Amount, decimals.Price, decimals.Qty, decimals.RoundingMode, decimals.IsForeign).Should().Be((0, 3, 1, "DOWN", true));
        decimals.ColumnFormats["soPrc"].Should().Be("N3");
    }
}

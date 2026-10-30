using System.Globalization;
using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Tax;

public class TaxCalculatorTests
{
    private const long Vat10 = 1;
    private const long Exempt = 2;
    private const long Vat10Included = 3;

    private static readonly TaxCalculator Calculator = new(new AmountRounder([new Currency("USD", 2), new Currency("KRW", 0)]));

    private static readonly Dictionary<long, TaxCodeRate> Rates = new()
    {
        [Vat10] = new(Vat10, 10m, false),
        [Exempt] = new(Exempt, 0m, false),
        [Vat10Included] = new(Vat10Included, 10m, true),
    };

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static TaxResult Calc(string mode, string currency, RoundingMode rounding, long taxCodeId, params string[] amounts) =>
        Calculator.Calculate([.. amounts.Select(a => new TaxLineInput(taxCodeId, D(a)))], Rates, mode, currency, rounding);

    [Fact, Trait("Id", "BC-UT-541")]
    public void Header_IsTheSumOfTheLines_AndTotalIsSupplyPlusTax()
    {
        var result = Calculator.Calculate(
            [new(Vat10, 100m), new(Exempt, 50m), new(Vat10, D("33.33"))], Rates, TaxCalcMode.Document, "USD", RoundingMode.HalfUp);

        result.Lines.Should().Equal(
            new TaxLineResult(Vat10, 100m, 10m), new TaxLineResult(Exempt, 50m, 0m), new TaxLineResult(Vat10, D("33.33"), D("3.33")));
        result.SupplyAmt.Should().Be(D("183.33"));
        result.TaxAmt.Should().Be(D("13.33"));
        result.TotalAmt.Should().Be(D("196.66"));
    }

    [Theory, Trait("Id", "BC-UT-543")]
    [InlineData(TaxCalcMode.Document, new[] { "11", "11", "20" })]
    [InlineData(TaxCalcMode.Line, new[] { "11", "11", "21" })]
    public void Mode_DocumentRoundsOncePerTaxCode_AndGivesTheRemainderToTheLargestLine_LineRoundsEachLine(string mode, string[] taxes)
    {
        // 415 × 10% = 41.5 → 42 once; shares 10.63, 10.63, 20.75 round to 11 + 11 + 21 = 43, so the largest line gives back 1
        var result = Calc(mode, "KRW", RoundingMode.HalfUp, Vat10, "105", "105", "205");

        result.Lines.Select(l => l.TaxAmt).Should().Equal(taxes.Select(D));
    }

    [Fact, Trait("Id", "BC-UT-543")]
    public void Document_RemainderOnATie_GoesToTheFirstLargestLine()
    {
        // 0.15 × 10% = 0.015 → 0.02; shares 0.0067 each round to 0.01 × 3 = 0.03
        var result = Calc(TaxCalcMode.Document, "USD", RoundingMode.HalfUp, Vat10, "0.05", "0.05", "0.05");

        result.Lines.Select(l => l.TaxAmt).Should().Equal(0m, D("0.01"), D("0.01"));
        result.TaxAmt.Should().Be(D("0.02"));
    }

    [Theory, Trait("Id", "BC-UT-543")]
    [InlineData(TaxCalcMode.Document, new[] { "-11", "-20" })]
    [InlineData(TaxCalcMode.Line, new[] { "-11", "-21" })]
    public void NegativeAmounts_KeepTheirSign(string mode, string[] taxes)
    {
        var result = Calc(mode, "KRW", RoundingMode.HalfUp, Vat10, "-105", "-205");

        result.Lines.Select(l => l.TaxAmt).Should().Equal(taxes.Select(D));
        result.SupplyAmt.Should().Be(-310m);
    }

    [Fact, Trait("Id", "BC-UT-543")]
    public void Document_ZeroAmounts_HaveNoTax()
    {
        Calc(TaxCalcMode.Document, "USD", RoundingMode.HalfUp, Vat10, "0", "0").Lines.Select(l => l.TaxAmt).Should().Equal(0m, 0m);
    }

    [Theory, Trait("Id", "BC-UT-544")]
    [InlineData("11000", RoundingMode.HalfUp, "10000", "1000")]
    [InlineData("10500", RoundingMode.HalfUp, "9545", "955")]
    [InlineData("10500", RoundingMode.Down, "9546", "954")]
    public void PriceIncluded_LineAmountHoldsTheTax_SupplyIsWhatIsLeft(string amount, RoundingMode rounding, string supply, string tax)
    {
        var line = Calc(TaxCalcMode.Line, "KRW", rounding, Vat10Included, amount).Lines.Single();

        line.SupplyAmt.Should().Be(D(supply));
        line.TaxAmt.Should().Be(D(tax));
    }

    [Fact, Trait("Id", "BC-UT-544")]
    public void PriceIncluded_Document_SplitsTheTaxOfTheSum()
    {
        // 21000 × 10/110 = 1909.09 → 1909; shares 954.5 → 955 each, so the first line gives back 1
        var result = Calc(TaxCalcMode.Document, "KRW", RoundingMode.HalfUp, Vat10Included, "10500", "10500");

        result.Lines.Select(l => (l.SupplyAmt, l.TaxAmt)).Should().Equal((9546m, 954m), (9545m, 955m));
        result.TotalAmt.Should().Be(21000m);
    }

    [Theory, Trait("Id", "BC-UT-545")]
    [InlineData(TaxCalcMode.Document, RoundingMode.HalfUp, "42")]
    [InlineData(TaxCalcMode.Document, RoundingMode.Down, "41")]
    [InlineData(TaxCalcMode.Line, RoundingMode.HalfUp, "43")]
    [InlineData(TaxCalcMode.Line, RoundingMode.Down, "40")]
    public void Tax_UsesTheModuleRoundingMode_AndTheCurrencyDecimals(string mode, RoundingMode rounding, string tax)
    {
        Calc(mode, "KRW", rounding, Vat10, "105", "105", "205").TaxAmt.Should().Be(D(tax));
    }

    [Fact, Trait("Id", "BC-UT-545")]
    public void UnknownCurrency_IsBcE412()
    {
        var act = () => Calc(TaxCalcMode.Line, "EUR", RoundingMode.HalfUp, Vat10, "1");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(AmountRounder.UnknownCurrencyCode);
    }

    [Fact]
    public void UnknownModeOrTaxCode_IsAProgrammingError()
    {
        var mode = () => Calc("ROW", "KRW", RoundingMode.HalfUp, Vat10, "1");
        var code = () => Calc(TaxCalcMode.Line, "KRW", RoundingMode.HalfUp, 99, "1");

        mode.Should().Throw<ArgumentException>();
        code.Should().Throw<ArgumentException>();
    }
}

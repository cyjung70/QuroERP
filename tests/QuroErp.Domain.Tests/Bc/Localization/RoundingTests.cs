using System.Globalization;
using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Localization;

public class RoundingTests
{
    private static readonly AmountRounder Rounder = new([new Currency("USD", 2), new Currency("KRW", 0)]);

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory, Trait("Id", "BC-UT-421")]
    [InlineData("10.005", "USD", "10.01")]
    [InlineData("10.004", "USD", "10.00")]
    [InlineData("-10.005", "USD", "-10.01")]
    [InlineData("1500.5", "KRW", "1501")]
    [InlineData("1500.4", "KRW", "1500")]
    public void AmountRounder_HalfUp_RoundsToCurrencyDecimals(string amount, string currency, string expected)
    {
        Rounder.Round(D(amount), currency)
            .Should().Be(D(expected));
    }

    [Theory, Trait("Id", "BC-UT-424")]
    [InlineData("10.009", "USD", "10.00")]
    [InlineData("1500.9", "KRW", "1500")]
    [InlineData("-1500.9", "KRW", "-1500")]
    public void AmountRounder_Down_CutsTowardZero(string amount, string currency, string expected)
    {
        Rounder.Round(D(amount), currency, RoundingMode.Down)
            .Should().Be(D(expected));
    }

    [Fact, Trait("Id", "BC-UT-421")]
    public void AmountRounder_DecimalsOf_UsdTwoKrwZero()
    {
        Rounder.DecimalsOf("USD").Should().Be(2);
        Rounder.DecimalsOf("KRW").Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-412")]
    public void AmountRounder_UnknownCurrency_ThrowsBcE412()
    {
        var act = () => Rounder.Round(1m, "EUR");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(AmountRounder.UnknownCurrencyCode);
    }

    [Theory, Trait("Id", "BC-UT-411")]
    [InlineData("usd", 2)]
    [InlineData("US", 2)]
    [InlineData("USD", -1)]
    [InlineData("USD", 5)]
    public void Currency_InvalidCodeOrDecimals_ThrowsBcE411(string code, int decimals)
    {
        var act = () => new Currency(code, decimals);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(Currency.InvalidCode);
    }

    [Theory, Trait("Id", "BC-UT-424")]
    [InlineData("HALF_UP", RoundingMode.HalfUp)]
    [InlineData("DOWN", RoundingMode.Down)]
    public void ParseMode_KnownCodes_RoundTrip(string code, RoundingMode expected)
    {
        Rounding.ParseMode(code).Should().Be(expected);
        Rounding.ToCode(expected).Should().Be(code);
    }

    [Theory, Trait("Id", "BC-UT-424")]
    [InlineData("HALF_EVEN")]
    [InlineData("half_up")]
    [InlineData(null)]
    public void ParseMode_OtherValue_ThrowsBcE424(string? code)
    {
        var act = () => Rounding.ParseMode(code);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(Rounding.InvalidModeCode);
    }

    [Theory, Trait("Id", "BC-UT-423")]
    [InlineData("1", 0)]
    [InlineData("1.50", 1)]
    [InlineData("0.0001", 4)]
    [InlineData("-2.125", 3)]
    public void DecimalPlaces_IgnoresTrailingZeros(string value, int expected)
    {
        Rounding.DecimalPlaces(D(value)).Should().Be(expected);
    }
}

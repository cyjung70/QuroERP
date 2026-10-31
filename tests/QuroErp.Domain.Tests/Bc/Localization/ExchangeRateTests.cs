using System.Globalization;
using FluentAssertions;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Tests.Bc.Localization;

public class ExchangeRateTests
{
    private static readonly AmountRounder Rounder = new([new Currency("USD", 2), new Currency("KRW", 0)]);

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory, Trait("Id", "BC-UT-412")]
    [InlineData("1350")]
    [InlineData("0.0007407407")]
    [InlineData("1")]
    public void Rate_UpTo10Decimals_IsAccepted(string rate)
    {
        var act = () => ExchangeRate.EnsureDecimals(D(rate), "exchangeRate");

        act.Should().NotThrow();
    }

    [Fact, Trait("Id", "BC-UT-412")]
    public void Rate_With11Decimals_IsRejected()
    {
        var act = () => ExchangeRate.EnsureDecimals(D("0.00074074074"), "exchangeRate");

        var error = act.Should().Throw<DomainException>().Which;
        (error.Code, error.Args).Should().BeEquivalentTo((DecimalPolicy.TooManyDecimalsCode, new object[] { "exchangeRate", 10 }));
    }

    [Theory, Trait("Id", "BC-UT-413")]
    [InlineData("100", "1350", "KRW", "135000")]
    [InlineData("135000", "0.0007407407", "USD", "100.00")]
    [InlineData("123.45", "1", "USD", "123.45")]
    public void BaseAmount_IsAmountTimesRate_RoundedToTheBaseCurrency(string amount, string rate, string baseCurrency, string expected)
    {
        ExchangeRate.BaseAmount(D(amount), D(rate), baseCurrency, Rounder).Should().Be(D(expected));
    }

    [Theory, Trait("Id", "BC-UT-414"), Trait("Id", "SD-UT-722")]
    [InlineData("100", "1350", "1300", "5000")]
    [InlineData("100", "1300", "1350", "-5000")]
    [InlineData("0.03", "1350", "1340", "1")]
    public void FxDiff_IsAppliedAtPaymentRateMinusAtDocumentRate_EachRounded(string applied, string paymentRate, string documentRate, string expected)
    {
        ExchangeRate.FxDiff(D(applied), D(paymentRate), D(documentRate), "KRW", Rounder).Should().Be(D(expected));
    }
}

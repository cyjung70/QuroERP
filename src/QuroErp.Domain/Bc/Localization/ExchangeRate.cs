using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>
/// Exchange rates are base currency per one unit of the document currency, with up to 10 decimals (BC-BR-412, SD-BR-907):
/// a KR company's USD document has 1350, a US company's KRW document 0.0007407407. A base currency document has rate 1.
/// Whether a rate must be above zero is each document's own rule.
/// </summary>
public static class ExchangeRate
{
    public const int Decimals = 10;

    /// <summary>The rate of a document in the base currency.</summary>
    public const decimal BaseCurrencyRate = 1m;

    /// <summary>Checks an entered rate.</summary>
    /// <param name="field">Field name for the message (e.g. exchangeRate).</param>
    /// <exception cref="DomainException">BC-E-423 with field and 10 when the rate has more decimals.</exception>
    public static void EnsureDecimals(decimal rate, string field)
    {
        if (Rounding.DecimalPlaces(rate) > Decimals)
        {
            throw new DomainException(DecimalPolicy.TooManyDecimalsCode, field, Decimals);
        }
    }

    /// <summary>Base currency amount (*_base_amt) = document amount × rate, rounded to the base currency decimals (BC-BR-413).</summary>
    /// <param name="mode">Rounding mode of the document's module (BC-BR-424).</param>
    /// <exception cref="DomainException">BC-E-412 when the base currency is not in the currency master.</exception>
    public static decimal BaseAmount(
        decimal amount, decimal rate, string baseCurrencyCode, IAmountRounder rounder, RoundingMode mode = RoundingMode.HalfUp) =>
        rounder.Round(amount * rate, baseCurrencyCode, mode);

    /// <summary>
    /// Exchange difference of a clearing line (fx_diff_base_amt, BC-BR-414, SD-BR-722, MM-BR-965): the applied amount in base
    /// currency at the receipt or payment rate minus the same at the original document rate, each side rounded.
    /// </summary>
    /// <exception cref="DomainException">BC-E-412 when the base currency is not in the currency master.</exception>
    public static decimal FxDiff(decimal appliedAmount, decimal paymentRate, decimal documentRate, string baseCurrencyCode, IAmountRounder rounder) =>
        BaseAmount(appliedAmount, paymentRate, baseCurrencyCode, rounder) - BaseAmount(appliedAmount, documentRate, baseCurrencyCode, rounder);
}

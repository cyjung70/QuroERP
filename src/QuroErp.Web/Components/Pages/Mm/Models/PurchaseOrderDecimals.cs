using QuroErp.Contracts.Bc.Localization;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// Decimals and modes of a purchase order in one currency (GET api/bc/localization, localization.md "자릿수"): amounts by the
/// document currency (BC-BR-421), unit prices by the currency's country × MM (BC-BR-422), quantities and the rounding mode
/// by the MM setting (BC-BR-423, BC-BR-424), and the base currency for the base total (BC-BR-413).
/// </summary>
public sealed record PurchaseOrderDecimals(
    string CurrencyCode, int Amount, int Price, int Qty, string RoundingMode, string TaxCalcMode, string BaseCurrencyCode, int BaseAmount)
{
    public const string Module = "MM";
    private const int Fallback = 2;

    /// <param name="currencyCode">The document currency; null is the base currency.</param>
    public static PurchaseOrderDecimals Of(LocalizationDto localization, string? currencyCode)
    {
        var code = string.IsNullOrEmpty(currencyCode) ? localization.BaseCurrencyCode : currencyCode;
        var currency = localization.Currencies.FirstOrDefault(c => c.CurrencyCode == code);
        var country = currency?.CountryCode ?? localization.CountryCode;
        var price = localization.CountryDecimals.FirstOrDefault(d => d.CountryCode == country && d.Module == Module)
                    ?? localization.CountryDecimals.FirstOrDefault(d => d.CountryCode == localization.CountryCode && d.Module == Module);
        var setting = localization.DecimalSettings.FirstOrDefault(d => d.Module == Module);
        return new PurchaseOrderDecimals(
            code,
            currency?.Decimals ?? Fallback,
            price?.UnitPriceDecimals ?? Fallback,
            setting?.QtyDecimals ?? Fallback,
            setting?.RoundingMode ?? PurchaseOrderCalculator.HalfUp,
            localization.TaxCalcMode,
            localization.BaseCurrencyCode,
            localization.Currencies.FirstOrDefault(c => c.CurrencyCode == localization.BaseCurrencyCode)?.Decimals ?? Fallback);
    }

    /// <summary>The document currency is not the base currency, so the base total is shown (BC-BR-413).</summary>
    public bool IsForeign => CurrencyCode != BaseCurrencyCode;

    /// <summary>Number formats of the line columns.</summary>
    public IReadOnlyDictionary<string, string> ColumnFormats => new Dictionary<string, string>
    {
        ["poQty"] = N(Qty),
        ["receivedQty"] = N(Qty),
        ["poPrc"] = N(Price),
        ["poAmt"] = N(Amount),
        ["taxAmt"] = N(Amount),
    };

    private static string N(int decimals) => "N" + decimals.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

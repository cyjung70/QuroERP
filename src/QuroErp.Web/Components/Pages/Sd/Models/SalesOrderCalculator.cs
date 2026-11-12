using QuroErp.Contracts.Bc.Localization;
using QuroErp.Web.Components.Pages.Mm.Models;

namespace QuroErp.Web.Components.Pages.Sd.Models;

/// <summary>Amounts of one sales order line for display: gross (qty × price), discount and amount (SD-BR-005).</summary>
public sealed record SoLineAmounts(decimal Gross, decimal Discount, decimal Amount);

/// <summary>Inputs of one line for the display totals. TaxCodeId groups lines for DOCUMENT tax (BC-BR-543).</summary>
public sealed record SoLineInput(decimal Qty, decimal Price, decimal DcRate, long TaxCodeId, decimal TaxRate, bool PriceIncluded = false);

/// <summary>Display totals of the kept lines: subtotal (gross), discount, supply, tax and total = supply + tax (SD-BR-034).</summary>
public sealed record SoDisplayTotals(decimal Subtotal, decimal Discount, decimal Supply, decimal Tax, decimal Total);

/// <summary>
/// The sales order screen's display math (SD-BR-005, SD-BR-032, SD-BR-034, BC-BR-413, BC-BR-541~545). The Web project references
/// only Contracts, so this mirrors the Domain rules for the preview; the API calculates and stores the amounts again. The
/// discount rate is a negative percentage (-10 = 10% off, SD-BR-404).
/// </summary>
public static class SalesOrderCalculator
{
    /// <summary>Amount = round(qty × price × (1 + rate / 100)); the discount is what the rate takes off the rounded gross.</summary>
    public static SoLineAmounts Line(decimal qty, decimal price, decimal dcRate, int decimals, string roundingMode)
    {
        var gross = PurchaseOrderCalculator.Round(qty * price, decimals, roundingMode);
        var amount = PurchaseOrderCalculator.Round(qty * price * (1m + dcRate / 100m), decimals, roundingMode);
        return new SoLineAmounts(gross, gross - amount, amount);
    }

    /// <summary>Tax of one line amount (LINE mode or one line alone).</summary>
    public static decimal Tax(decimal amount, decimal rate, bool priceIncluded, int decimals, string roundingMode) =>
        PurchaseOrderCalculator.Tax(amount, rate, priceIncluded, decimals, roundingMode);

    /// <summary>
    /// Totals of the lines kept (lines marked for delete are left out by the caller). LINE taxes each line; DOCUMENT taxes the
    /// sum of each tax code once (BC-BR-543). A price that includes the tax puts the tax inside the amount (BC-BR-544).
    /// </summary>
    public static SoDisplayTotals Totals(IEnumerable<SoLineInput> lines, int decimals, string taxCalcMode, string roundingMode)
    {
        var list = lines.ToList();
        var amounts = list.Select(l => Line(l.Qty, l.Price, l.DcRate, decimals, roundingMode)).ToList();
        decimal tax = 0, supply = 0;
        foreach (var group in Enumerable.Range(0, list.Count).GroupBy(i => list[i].TaxCodeId))
        {
            var first = list[group.First()];
            var groupAmount = group.Sum(i => amounts[i].Amount);
            var groupTax = taxCalcMode == PurchaseOrderCalculator.DocumentMode
                ? Tax(groupAmount, first.TaxRate, first.PriceIncluded, decimals, roundingMode)
                : group.Sum(i => Tax(amounts[i].Amount, first.TaxRate, first.PriceIncluded, decimals, roundingMode));
            tax += groupTax;
            supply += first.PriceIncluded ? groupAmount - groupTax : groupAmount;
        }
        return new SoDisplayTotals(amounts.Sum(a => a.Gross), amounts.Sum(a => a.Discount), supply, tax, supply + tax);
    }

    /// <summary>Total in the base currency: total × rate, rounded to the base currency decimals (BC-BR-413).</summary>
    public static decimal BaseTotal(decimal total, decimal exchangeRate, int baseDecimals, string roundingMode) =>
        PurchaseOrderCalculator.Round(total * exchangeRate, baseDecimals, roundingMode);
}

/// <summary>
/// Decimals and modes of a sales order in one currency (GET api/bc/localization, localization.md "자릿수"): amounts by the document
/// currency (BC-BR-421), unit prices by the currency's country × SD (BC-BR-422), quantities and the rounding mode by the SD
/// setting (BC-BR-423, BC-BR-424), and the base currency for the base total (BC-BR-413).
/// </summary>
public sealed record SalesOrderDecimals(
    string CurrencyCode, int Amount, int Price, int Qty, string RoundingMode, string TaxCalcMode, string BaseCurrencyCode, int BaseAmount)
{
    public const string Module = "SD";
    private const int Fallback = 2;

    /// <param name="currencyCode">The document currency; null is the base currency.</param>
    public static SalesOrderDecimals Of(LocalizationDto localization, string? currencyCode)
    {
        var code = string.IsNullOrEmpty(currencyCode) ? localization.BaseCurrencyCode : currencyCode;
        var currency = localization.Currencies.FirstOrDefault(c => c.CurrencyCode == code);
        var country = currency?.CountryCode ?? localization.CountryCode;
        var price = localization.CountryDecimals.FirstOrDefault(d => d.CountryCode == country && d.Module == Module)
                    ?? localization.CountryDecimals.FirstOrDefault(d => d.CountryCode == localization.CountryCode && d.Module == Module);
        var setting = localization.DecimalSettings.FirstOrDefault(d => d.Module == Module);
        return new SalesOrderDecimals(
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
        ["soQty"] = N(Qty),
        ["shipQty"] = N(Qty),
        ["dnQty"] = N(Qty),
        ["soPrc"] = N(Price),
        ["soAmt"] = N(Amount),
        ["taxAmt"] = N(Amount),
    };

    private static string N(int decimals) => "N" + decimals.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

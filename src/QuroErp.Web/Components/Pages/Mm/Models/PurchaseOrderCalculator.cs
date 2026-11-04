namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>Amounts of one purchase order line for display: gross (qty × price), discount and amount (gross − discount).</summary>
public sealed record PoLineAmounts(decimal Gross, decimal Discount, decimal Amount);

/// <summary>Inputs of one line for the display totals. TaxCodeId groups lines for DOCUMENT tax (BC-BR-543).</summary>
public sealed record PoLineInput(decimal Qty, decimal Price, decimal DcRate, long TaxCodeId, decimal TaxRate, bool PriceIncluded = false);

/// <summary>Display totals of the kept lines: subtotal (gross), discount, supply, tax and total = supply + tax.</summary>
public sealed record PoDisplayTotals(decimal Subtotal, decimal Discount, decimal Supply, decimal Tax, decimal Total);

/// <summary>
/// The purchase order screen's display math (MM-BR-015, MM-BR-025, MM-BR-032, BC-BR-413, BC-BR-541~545). The Web project
/// references only Contracts, so this mirrors the Domain rules for the preview; the API calculates and stores the amounts again.
/// Amounts round to the document currency decimals with the MM rounding mode; quantity and price are not rounded.
/// </summary>
public static class PurchaseOrderCalculator
{
    public const string LineMode = "LINE";
    public const string DocumentMode = "DOCUMENT";
    public const string HalfUp = "HALF_UP";
    public const string Down = "DOWN";

    /// <summary>HALF_UP rounds half away from zero; DOWN cuts toward zero (BC-BR-424).</summary>
    public static decimal Round(decimal value, int decimals, string roundingMode) =>
        Math.Round(value, decimals, roundingMode == Down ? MidpointRounding.ToZero : MidpointRounding.AwayFromZero);

    public static PoLineAmounts Line(decimal qty, decimal price, decimal dcRate, int decimals, string roundingMode)
    {
        var gross = Round(qty * price, decimals, roundingMode);
        var discount = Round(gross * dcRate / 100m, decimals, roundingMode);
        return new PoLineAmounts(gross, discount, gross - discount);
    }

    /// <summary>Tax of an amount: amount × rate, or amount × rate / (100 + rate) when the price includes the tax (BC-BR-544).</summary>
    public static decimal Tax(decimal amount, decimal rate, bool priceIncluded, int decimals, string roundingMode) =>
        Round(amount * rate / (priceIncluded ? 100m + rate : 100m), decimals, roundingMode);

    /// <summary>
    /// Totals of the lines kept (lines marked for delete are left out by the caller). LINE taxes each line; DOCUMENT taxes the
    /// sum of each tax code once (BC-BR-543).
    /// </summary>
    public static PoDisplayTotals Totals(IEnumerable<PoLineInput> lines, int decimals, string taxCalcMode, string roundingMode)
    {
        var list = lines.ToList();
        var amounts = list.Select(l => Line(l.Qty, l.Price, l.DcRate, decimals, roundingMode)).ToList();
        decimal tax = 0, supply = 0;
        foreach (var group in Enumerable.Range(0, list.Count).GroupBy(i => list[i].TaxCodeId))
        {
            var first = list[group.First()];
            var groupTax = taxCalcMode == DocumentMode
                ? Tax(group.Sum(i => amounts[i].Amount), first.TaxRate, first.PriceIncluded, decimals, roundingMode)
                : group.Sum(i => Tax(amounts[i].Amount, first.TaxRate, first.PriceIncluded, decimals, roundingMode));
            var groupAmount = group.Sum(i => amounts[i].Amount);
            tax += groupTax;
            supply += first.PriceIncluded ? groupAmount - groupTax : groupAmount;
        }
        return new PoDisplayTotals(amounts.Sum(a => a.Gross), amounts.Sum(a => a.Discount), supply, tax, supply + tax);
    }

    /// <summary>Total in the base currency: total × rate, rounded to the base currency decimals (BC-BR-413).</summary>
    public static decimal BaseTotal(decimal total, decimal exchangeRate, int baseDecimals, string roundingMode) =>
        Round(total * exchangeRate, baseDecimals, roundingMode);
}

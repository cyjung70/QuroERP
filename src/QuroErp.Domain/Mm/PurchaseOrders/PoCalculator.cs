using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Domain.Mm.PurchaseOrders;

/// <summary>Amounts of one line (MM-BR-015): gross_amt, dc_amt and po_amt (= supply amount, MM-BR-032).</summary>
public sealed record PoLineAmounts(decimal Gross, decimal Discount, decimal Net);

/// <summary>Header amounts before tax: subtotal (sum of gross_amt), discount (sum of dc_amt), net (sum of supply amounts).</summary>
public sealed record PoTotals(decimal Subtotal, decimal Discount, decimal Net);

/// <summary>
/// Purchase order line amounts and totals (MM-BR-015, MM-BR-025). The server calculates them on save; the screen
/// only previews. Amounts round to the document currency decimals with HALF_UP (BC-BR-421, BC-BR-424); quantity and
/// unit price are entered values and are not rounded here (BC-BR-423).
/// Tax and the header total (tot_po_amt, MM-BR-032) come from the tax calculator (BC-BR-541~545).
/// </summary>
public static class PoCalculator
{
    /// <param name="amountDecimals">Decimals of the document currency (IAmountRounder.DecimalsOf).</param>
    public static PoLineAmounts CalcLine(PoLine line, int amountDecimals)
    {
        var gross = Rounding.Round(line.Qty * line.UnitPrice, amountDecimals, RoundingMode.HalfUp);
        var discount = Rounding.Round(gross * line.DiscountRate / 100m, amountDecimals, RoundingMode.HalfUp);
        return new PoLineAmounts(gross, discount, gross - discount);
    }

    /// <summary>Sums the rounded line amounts; lines marked for delete are left out.</summary>
    public static PoTotals CalcTotals(IEnumerable<PoLine> lines, int amountDecimals)
    {
        decimal subtotal = 0, discount = 0;
        foreach (var line in lines.Where(l => !l.MarkedForDelete))
        {
            var amounts = CalcLine(line, amountDecimals);
            subtotal += amounts.Gross;
            discount += amounts.Discount;
        }
        return new PoTotals(subtotal, discount, subtotal - discount);
    }
}

namespace QuroERP.Web.Features.MM.PurchaseOrder;

/// <summary>
/// Pure amount calculation for a PO. No I/O, so it is unit-tested directly.
/// The API repeats the same calculation on save; the server result wins.
/// </summary>
public static class PoCalculator
{
    /// Rounding rule: MidpointRounding.AwayFromZero (commercial rounding), at each line, then summed.
    private static decimal R(decimal v, int places) => Math.Round(v, places, MidpointRounding.AwayFromZero);

    public static void CalcLine(PoLine l, CurrencyPrecision p)
    {
        var qty = R(l.Qty, p.Qty);
        var price = R(l.UnitPrice, p.Price);
        l.GrossAmount = R(qty * price, p.Amount);
        l.DiscountAmount = R(l.GrossAmount * l.DiscountRate / 100m, p.Amount);
        l.NetAmount = l.GrossAmount - l.DiscountAmount;
        l.TaxAmount = R(l.NetAmount * l.TaxRate / 100m, p.Amount);
    }

    /// Lines marked for delete are excluded from totals.
    public static PoTotals CalcTotals(IEnumerable<PoLine> lines, decimal exchangeRate, CurrencyPrecision p, int localAmountPlaces = 0)
    {
        decimal sub = 0, disc = 0, tax = 0;
        foreach (var l in lines)
        {
            if (l.State == RowState.MarkedForDelete) continue;
            CalcLine(l, p);
            sub += l.GrossAmount;
            disc += l.DiscountAmount;
            tax += l.TaxAmount;
        }
        var net = sub - disc;
        var total = net + tax;
        return new PoTotals(sub, disc, net, tax, total, R(total * exchangeRate, localAmountPlaces));
    }
}

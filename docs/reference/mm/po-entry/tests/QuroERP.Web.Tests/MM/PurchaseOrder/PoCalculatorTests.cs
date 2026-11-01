using QuroERP.Web.Features.MM.PurchaseOrder;
using Xunit;

namespace QuroERP.Web.Tests.MM.PurchaseOrder;

public class PoCalculatorTests
{
    private static readonly CurrencyPrecision Usd = new("USD", Qty: 2, Price: 4, Amount: 2);

    private static PoLine Line(decimal qty, decimal price, decimal disc = 0, decimal tax = 10, RowState s = RowState.Added) =>
        new() { Qty = qty, UnitPrice = price, DiscountRate = disc, TaxRate = tax, State = s };

    [Fact]
    public void Line_amount_is_qty_times_price_rounded_to_amount_places()
    {
        var l = Line(3m, 1.3333m);
        PoCalculator.CalcLine(l, Usd);
        Assert.Equal(4.00m, l.GrossAmount); // 3.9999 → 4.00
    }

    [Fact]
    public void Discount_is_rounded_half_away_from_zero_and_net_is_gross_minus_discount()
    {
        var l = Line(5m, 280.5m, disc: 5m);
        PoCalculator.CalcLine(l, Usd);
        Assert.Equal(1402.50m, l.GrossAmount);
        Assert.Equal(70.13m, l.DiscountAmount);  // 70.125 → 70.13
        Assert.Equal(1332.37m, l.NetAmount);
        Assert.Equal(133.24m, l.TaxAmount);      // 133.237 → 133.24
    }

    [Fact]
    public void Totals_match_the_mockup_and_exclude_rows_marked_for_delete()
    {
        var lines = new[]
        {
            Line(10m, 1250m),
            Line(5m, 280.5m, disc: 5m),
            Line(20m, 45m, s: RowState.MarkedForDelete)
        };
        var t = PoCalculator.CalcTotals(lines, exchangeRate: 1382.5m, Usd, localAmountPlaces: 0);
        Assert.Equal(13902.50m, t.Subtotal);
        Assert.Equal(70.13m, t.Discount);
        Assert.Equal(13832.37m, t.Net);
        Assert.Equal(1383.24m, t.Tax);
        Assert.Equal(15215.61m, t.Total);
        Assert.Equal(21035581m, t.TotalLocal);   // 21,035,580.825 → 21,035,581
    }

    [Fact]
    public void Krw_amounts_have_no_decimals()
    {
        var krw = new CurrencyPrecision("KRW", Qty: 2, Price: 4, Amount: 0);
        var l = Line(3m, 333.3333m, tax: 10m);
        PoCalculator.CalcLine(l, krw);
        Assert.Equal(1000m, l.GrossAmount);      // 999.9999 → 1000
        Assert.Equal(100m, l.TaxAmount);
    }
}

public class PoStatusRulesTests
{
    private static PoDocument Doc(PoStatus s, decimal received = 0, string? poNo = "PO261000012") => new()
    {
        Header = new PoHeader { PoNo = poNo, Status = s },
        Lines = { new PoLine { Seq = 1, ItemCode = "COMIX010", Qty = 10, ReceivedQty = received } }
    };

    [Theory]
    [InlineData(PoStatus.Draft, true)]
    [InlineData(PoStatus.Approved, true)]
    [InlineData(PoStatus.PartiallyReceived, false)]
    [InlineData(PoStatus.Received, false)]
    [InlineData(PoStatus.Closed, false)]
    [InlineData(PoStatus.Cancelled, false)]
    public void Editable_only_before_any_receipt(PoStatus s, bool expected) =>
        Assert.Equal(expected, PoStatusRules.IsEditable(Doc(s)));

    [Fact]
    public void Any_received_quantity_locks_edit_and_delete_even_if_status_lags()
    {
        var d = Doc(PoStatus.Approved, received: 1);
        Assert.False(PoStatusRules.CanSave(d));
        Assert.False(PoStatusRules.CanDelete(d));
        Assert.False(PoStatusRules.CanCancel(d));
        Assert.True(PoStatusRules.CanClose(d));
    }

    [Fact]
    public void New_unsaved_po_cannot_be_approved_deleted_or_printed()
    {
        var d = Doc(PoStatus.Draft, poNo: null);
        Assert.True(PoStatusRules.CanSave(d));
        Assert.False(PoStatusRules.CanApprove(d));
        Assert.False(PoStatusRules.CanDelete(d));
        Assert.False(PoStatusRules.CanPrint(d));
    }
}

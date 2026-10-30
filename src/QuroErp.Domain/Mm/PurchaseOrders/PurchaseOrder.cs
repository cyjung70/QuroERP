namespace QuroErp.Domain.Mm.PurchaseOrders;

/// <summary>One purchase order line (mm_purchase_order_line) as far as amounts and status rules need it.</summary>
/// <param name="Qty">po_qty, already checked against the MM quantity decimals (BC-BR-423).</param>
/// <param name="UnitPrice">po_prc in the document currency, already checked against the unit price decimals.</param>
/// <param name="DiscountRate">dc_rate in percent, 0–100 (MM-BR-025).</param>
/// <param name="ReceivedQty">Sum of goods receipt quantities of this line.</param>
/// <param name="MarkedForDelete">Marked for delete on the screen: deleted on save and left out of totals (MM-BR-015).</param>
public sealed record PoLine(
    int Seq, decimal Qty, decimal UnitPrice, decimal DiscountRate = 0, decimal ReceivedQty = 0, bool MarkedForDelete = false);

/// <summary>A purchase order header with its lines, as far as amounts and status rules need it.</summary>
/// <param name="PoNo">Null until the first save numbers it (MM-BR-017).</param>
public sealed record PurchaseOrder(string? PoNo, PoStatus Status, IReadOnlyList<PoLine> Lines)
{
    /// <summary>Lines that stay after save.</summary>
    public IEnumerable<PoLine> ActiveLines => Lines.Where(l => !l.MarkedForDelete);

    /// <summary>Any quantity received on any line (MM-BR-013: sum(receipt_qty) ≠ 0).</summary>
    public bool HasReceipt => Lines.Sum(l => l.ReceivedQty) != 0;
}

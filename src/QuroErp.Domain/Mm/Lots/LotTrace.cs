using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Domain.Mm.Lots;

/// <summary>What a row of the Lot trace stands for (MM-UC-040).</summary>
public static class LotTraceDocTypes
{
    /// <summary>A Lot itself: the traced Lot, a Set Lot it went into, or a Lot whose origin is not a receipt.</summary>
    public const string Lot = "LOT";

    public const string Receipt = "GR";

    public const string Shipment = "SHIP";

    /// <summary>A sales invoice or Credit Memo that moved stock itself (bill first, cancel).</summary>
    public const string Invoice = "INV";

    public const string Assembly = "ASM";

    /// <summary>Any other stock movement (transfer, count, unplanned receipt, return …); the movement type tells which.</summary>
    public const string Movement = "MOVE";
}

/// <summary>
/// A stock movement of a traced Lot with the document it belongs to: DocType from the document its reference names, PartyName
/// the receipt's vendor or the shipment's or invoice's customer.
/// </summary>
public sealed record LotTraceMovement(
    long LotId, string MovType, string? RefKey, int? RefSeq, DateOnly DocumentDt, long WarehouseId, string? WarehouseCode,
    long ItemId, string ItemCd, string? ItemName, string LotNo, decimal Qty, string DocType, string? PartyName);

/// <summary>
/// A Lot with its origin: the receipt line that created it (mm_lot.goods_receipt_line_id) or the assembly that made it as a Set
/// Lot (mm_lot.assembly_id).
/// </summary>
public sealed record LotTraceLot(
    long Id, long ItemId, string ItemCd, string? ItemName, string LotNo, DateOnly? ReceivedDt,
    string? ReceiptNo, int? ReceiptSeq, DateOnly? ReceiptDt, string? VendorName, decimal? ReceiptQty,
    string? AssemblyNo, DateOnly? AssemblyDt, decimal? AssemblyQty);

/// <summary>A component Lot that went into a Set Lot by an open assembly (mm_lot_link, MM-BR-368).</summary>
public sealed record LotTraceLink(long SetLotId, long ComponentLotId, string AssemblyNo, DateOnly AssemblyDt, decimal Qty);

/// <summary>
/// One row of the trace tree. Key numbers the rows from 1 in their order; ParentKey is the row above it in the tree. Qty is the
/// movement qty (signed: a receipt is above 0, a shipment below) or, on an origin row, the qty the receipt, assembly or link
/// gave; a Lot row without a qty of its own has none.
/// </summary>
public sealed record LotTraceRow(
    int Key, int? ParentKey, int Level, string DocType, string? DocNo, int? DocSeq, DateOnly? DocDt, string? MovType,
    string? WarehouseCode, string? PartyName, string ItemCd, string? ItemName, string LotNo, decimal? Qty);

/// <summary>
/// Lot trace rules MM-BR-366~369 (MM lot.md). Forward runs from a Lot to where it went, backward from an issue to where its Lots
/// came from; both cross between a Set Lot and its component Lots one step (MM-BR-368). Lot '*' is not traced.
/// </summary>
public static class LotTraceRules
{
    /// <summary>Lot '*' has no identity to follow (MM-BR-363); a trace of it is empty.</summary>
    public static bool IsTraceable(string? lotNo) => !string.IsNullOrWhiteSpace(lotNo) && lotNo.Trim() != StockKeyRules.System;

    /// <summary>
    /// The movements of a Lot per document line and warehouse, a reversal netted with what it cancels (MM-BR-366): rows that net
    /// to 0 are left out. A line split over Locations is one row. Order: date, then the first movement read.
    /// </summary>
    public static IReadOnlyList<LotTraceMovement> Net(IEnumerable<LotTraceMovement> movements) =>
        [.. movements
            .Select((m, i) => (Movement: m, Index: i))
            .GroupBy(x => (x.Movement.LotId, x.Movement.MovType, x.Movement.RefKey, x.Movement.RefSeq, x.Movement.WarehouseId))
            .Select(g => (First: g.OrderBy(x => x.Movement.DocumentDt).ThenBy(x => x.Index).First(), Qty: g.Sum(x => x.Movement.Qty)))
            .Where(x => x.Qty != 0)
            .OrderBy(x => x.First.Movement.DocumentDt).ThenBy(x => x.First.Index)
            .Select(x => x.First.Movement with { Qty = x.Qty })];

    /// <summary>
    /// Forward trace (MM-BR-366, MM-BR-368): the Lot, under it its netted movements, and under each assembly issue of it the Set
    /// Lot it went into with that Set Lot's netted movements. A Set Lot is not followed further.
    /// </summary>
    /// <param name="movements">Movements of the Lot and of the Set Lots in <paramref name="setLots"/>.</param>
    /// <param name="links">Links whose component Lot is the traced Lot.</param>
    public static IReadOnlyList<LotTraceRow> Forward(
        LotTraceLot lot, IEnumerable<LotTraceMovement> movements, IEnumerable<LotTraceLink> links, IEnumerable<LotTraceLot> setLots)
    {
        if (!IsTraceable(lot.LotNo))
        {
            return [];
        }
        var byLot = Net(movements).ToLookup(m => m.LotId);
        var setLotById = setLots.ToDictionary(l => l.Id);
        var linkList = links.Where(l => l.ComponentLotId == lot.Id).ToList();
        var rows = new TreeRows();
        var root = rows.Add(null, 0, LotRow(lot));
        foreach (var m in byLot[lot.Id])
        {
            var key = rows.Add(root, 1, MovementRow(m));
            if (m.MovType != MovementType.AssemblyIssue)
            {
                continue;
            }
            foreach (var link in linkList.Where(l => l.AssemblyNo == m.RefKey))
            {
                if (!setLotById.TryGetValue(link.SetLotId, out var set))
                {
                    continue;
                }
                var setKey = rows.Add(key, 2, new Shape(
                    LotTraceDocTypes.Lot, link.AssemblyNo, null, link.AssemblyDt, null, null, null, set.ItemCd, set.ItemName, set.LotNo, link.Qty));
                foreach (var s in byLot[set.Id])
                {
                    rows.Add(setKey, 3, MovementRow(s));
                }
            }
        }
        return rows.ToList();
    }

    /// <summary>
    /// Backward trace (MM-BR-367, MM-BR-368): each netted issue line of a traceable Lot, under it where the Lot came from (its
    /// receipt and vendor, or the assembly that made it), and under a Set Lot's assembly each component Lot with its own origin
    /// and the qty linked. A component Lot is not followed further.
    /// </summary>
    /// <param name="issues">Issue movements of shipments and invoices; increases are reversals and are netted.</param>
    /// <param name="lots">The Lots of the issues and their component Lots.</param>
    /// <param name="links">Links whose Set Lot is one of the issued Lots.</param>
    public static IReadOnlyList<LotTraceRow> Backward(
        IEnumerable<LotTraceMovement> issues, IEnumerable<LotTraceLot> lots, IEnumerable<LotTraceLink> links)
    {
        var lotById = lots.ToDictionary(l => l.Id);
        var linksBySet = links.ToLookup(l => l.SetLotId);
        var rows = new TreeRows();
        foreach (var m in Net(issues).Where(m => m.Qty < 0 && IsTraceable(m.LotNo)))
        {
            var key = rows.Add(null, 0, MovementRow(m));
            if (!lotById.TryGetValue(m.LotId, out var lot))
            {
                continue;
            }
            var originKey = rows.Add(key, 1, OriginRow(lot, null));
            if (lot.AssemblyNo is null)
            {
                continue;
            }
            foreach (var link in linksBySet[lot.Id])
            {
                if (lotById.TryGetValue(link.ComponentLotId, out var component) && IsTraceable(component.LotNo))
                {
                    rows.Add(originKey, 2, OriginRow(component, link.Qty));
                }
            }
        }
        return rows.ToList();
    }

    // The traced Lot: from its receipt if it has one
    private static Shape LotRow(LotTraceLot lot) => new(
        LotTraceDocTypes.Lot, lot.ReceiptNo ?? lot.AssemblyNo, lot.ReceiptSeq, lot.ReceivedDt, null, null, lot.VendorName,
        lot.ItemCd, lot.ItemName, lot.LotNo, null);

    // Where a Lot came from: its receipt, the assembly that made it, or the Lot alone. A linked component shows the linked qty
    private static Shape OriginRow(LotTraceLot lot, decimal? linkedQty)
    {
        if (lot.ReceiptNo is not null)
        {
            return new(LotTraceDocTypes.Receipt, lot.ReceiptNo, lot.ReceiptSeq, lot.ReceiptDt, null, null, lot.VendorName,
                lot.ItemCd, lot.ItemName, lot.LotNo, linkedQty ?? lot.ReceiptQty);
        }
        if (lot.AssemblyNo is not null)
        {
            return new(LotTraceDocTypes.Assembly, lot.AssemblyNo, null, lot.AssemblyDt, null, null, null,
                lot.ItemCd, lot.ItemName, lot.LotNo, linkedQty ?? lot.AssemblyQty);
        }
        return new(LotTraceDocTypes.Lot, null, null, lot.ReceivedDt, null, null, null, lot.ItemCd, lot.ItemName, lot.LotNo, linkedQty);
    }

    private static Shape MovementRow(LotTraceMovement m) => new(
        m.DocType, m.RefKey, m.RefSeq, m.DocumentDt, m.MovType, m.WarehouseCode, m.PartyName, m.ItemCd, m.ItemName, m.LotNo, m.Qty);

    private sealed record Shape(
        string DocType, string? DocNo, int? DocSeq, DateOnly? DocDt, string? MovType, string? WarehouseCode, string? PartyName,
        string ItemCd, string? ItemName, string LotNo, decimal? Qty);

    private sealed class TreeRows
    {
        private readonly List<LotTraceRow> _rows = [];

        public int Add(int? parentKey, int level, Shape s)
        {
            var key = _rows.Count + 1;
            _rows.Add(new LotTraceRow(
                key, parentKey, level, s.DocType, s.DocNo, s.DocSeq, s.DocDt, s.MovType, s.WarehouseCode, s.PartyName,
                s.ItemCd, s.ItemName, s.LotNo, s.Qty));
            return key;
        }

        public List<LotTraceRow> ToList() => _rows;
    }
}

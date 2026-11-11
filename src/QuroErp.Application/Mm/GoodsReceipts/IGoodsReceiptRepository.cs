using System.Data;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.PurchaseOrders;

namespace QuroErp.Application.Mm.GoodsReceipts;

/// <summary>mm_goods_receipt row with the vendor name.</summary>
public sealed record GoodsReceiptRow(
    long Id, string GrNo, long VendorId, string? VendorName, DateOnly GrDt, long WarehouseId, string? RefNo, string? Remarks,
    string CurrencyCode, decimal ExchangeRate, decimal SupplyAmt, decimal TaxAmt, decimal TotGrAmt, decimal TotBaseAmt,
    DateTimeOffset UpdatedAt, DateTimeOffset? CreatedAt, string? CreatedBy, string? UpdatedBy);

/// <summary>mm_goods_receipt_line row with the codes it names and the PO line it came from.</summary>
public sealed record GoodsReceiptLineRow(
    long Id, int Seq, long ItemId, string? ItemCd, string? ItemName, string? Unit, long WarehouseId, string? WarehouseCode, long? LotId,
    string? LotNo, decimal GrQty, decimal GrPrc, decimal GrAmt, long TaxCodeId, string? TaxCode, decimal SupplyAmt, decimal TaxAmt,
    bool Deductible, long? PurchaseOrderLineId, string? PoNo, int? PoSeq, decimal? PoQty, decimal? PoReceivedQty, decimal InvoicedQty,
    decimal ReturnedQty)
{
    public StoredGoodsReceiptLine ToStored() => new(
        Id, Seq, ItemId, ItemName, WarehouseId, LotId, GrQty, GrPrc, TaxCodeId, Deductible, PurchaseOrderLineId, InvoicedQty, ReturnedQty);
}

/// <summary>A row of the receipt list with the line quantities its status is computed from (MM-BR-105).</summary>
public sealed record GoodsReceiptSummaryRow(
    long Id, string GrNo, DateOnly GrDt, long VendorId, string? VendorName, string CurrencyCode, decimal TotGrAmt, bool AnyInvoiced,
    bool AllInvoiced);

public sealed record GoodsReceiptVendorRow(long Id, string? VName, string? CurrencyCode, long? TaxCodeId);

/// <summary>Header values to write; Lines are the new lines with their computed amounts.</summary>
public sealed record GoodsReceiptWrite(GoodsReceiptHeader Header, GoodsReceiptTotals Totals, IReadOnlyList<GoodsReceiptLine> Lines);

/// <summary>Goods receipts (mm_goods_receipt, mm_goods_receipt_line, MM-UC-004). Callers pass their own transaction.</summary>
public interface IGoodsReceiptRepository
{
    Task<IReadOnlyList<GoodsReceiptSummaryRow>> SearchAsync(
        GoodsReceiptSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="forUpdate">Locks the header row until the transaction ends.</param>
    Task<GoodsReceiptRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Lines in seq order.</summary>
    Task<IReadOnlyList<GoodsReceiptLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<GoodsReceiptVendorRow?> FindVendorAsync(long vendorId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Item type of each item that has one.</summary>
    Task<IReadOnlyDictionary<long, string>> FindItemTypesAsync(
        IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The given warehouses that are in use.</summary>
    Task<IReadOnlySet<long>> FindUsableWarehousesAsync(
        IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// MM-BR-703: the user's default warehouse when it is in use and not the consignment warehouse, else the company default.
    /// </summary>
    Task<long?> FindDefaultWarehouseAsync(string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>mm_inventory_setting.over_receipt_mode, null without a setting row (MM-BR-102).</summary>
    Task<string?> FindOverReceiptModeAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the purchase orders of the given PO lines in id order and reads those lines (MM-BR-108). Lines that do not exist are
    /// left out.
    /// </summary>
    Task<IReadOnlyList<ReceiptPoLine>> LockPoLinesAsync(
        IReadOnlyCollection<long> purchaseOrderLineIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Adds to mm_purchase_order_line.receipt_qty (negative to take back, MM-BR-108).</summary>
    Task AddReceiptQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByPoLine, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Status code, and ordered and received qty of every line, of each locked purchase order (MM-BR-018).</summary>
    Task<IReadOnlyList<(long PurchaseOrderId, string? Status, decimal PoQty, decimal ReceivedQty)>> FindPoQuantitiesAsync(
        IReadOnlyCollection<long> purchaseOrderIds, IDbTransaction transaction, CancellationToken cancellationToken);

    Task SetPoStatusAsync(long purchaseOrderId, PoStatus status, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>MM-BR-110: the standard purchase price of each item.</summary>
    Task SetStandardPricesAsync(
        IReadOnlyDictionary<long, decimal> priceByItem, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>The new header id.</returns>
    Task<long> InsertAsync(
        string grNo, long businessPlaceId, GoodsReceiptWrite receipt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the header, deletes the given lines, writes the supply and tax of the saved lines (computed again with the whole
    /// document, MM-BR-162) and inserts the new lines (Id null).
    /// </summary>
    /// <returns>False when the header changed after updatedAt.</returns>
    Task<bool> UpdateAsync(
        long id, GoodsReceiptWrite receipt, IReadOnlyCollection<long> deletedLineIds, DateTimeOffset updatedAt, string user,
        IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The lot each new line's GR movement used (MM-BR-353).</summary>
    Task SetLineLotsAsync(
        long id, IReadOnlyDictionary<int, long> lotBySeq, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Original GR movements of the given lines (ref_key = GR number, ref_seq = line) that are not reversed yet (MM-BR-601).</summary>
    Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(
        string grNo, IReadOnlyCollection<int> seqs, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when the header changed after updatedAt.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>MM-BR-113: lines of the vendor's PO and RE orders with remaining qty above 0, PO number and line order.</summary>
    Task<IReadOnlyList<OpenPoLineDto>> FindOpenPoLinesAsync(long vendorId, IDbTransaction transaction, CancellationToken cancellationToken);
}

using System.Data;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Domain.Mm.PurchaseOrders;

namespace QuroErp.Application.Mm.PurchaseOrders;

/// <summary>mm_purchase_order row with the vendor name and the quantity received on all its lines.</summary>
public sealed record PurchaseOrderRow(
    long Id, string PoNo, string Status, long VendorId, string? VendorName, DateOnly PoDt, string CurrencyCode, decimal ExchangeRate,
    string? Incoterms, DateOnly? DueDt, long? WarehouseId, string? PaymentTerms, string? Buyer, string? Remarks, decimal SupplyAmt,
    decimal TaxAmt, decimal TotPoAmt, decimal TotBaseAmt, int RevisionNo, DateTimeOffset? ApprovedAt, string? ApprovedBy,
    DateTimeOffset? ClosedAt, string? ClosedBy, DateTimeOffset? CancelledAt, string? CancelledBy, decimal ReceivedQty,
    DateTimeOffset UpdatedAt);

/// <summary>mm_purchase_order_line row with the item code and its received quantity (mm_goods_receipt_line.gr_qty).</summary>
public sealed record PurchaseOrderLineRow(
    long Id, int Seq, long ItemId, string? ItemCd, string? ItemName, decimal PoQty, decimal PoPrc, decimal GrossAmt, decimal DcRate,
    decimal DcAmt, decimal PoAmt, long TaxCodeId, decimal SupplyAmt, decimal TaxAmt, DateOnly? DueDt, long? WarehouseId,
    decimal ReceivedQty);

/// <summary>The vendor fields a purchase order defaults from (MM-BR-031, BC-BR-542).</summary>
public sealed record PurchaseOrderVendorRow(long Id, string? VName, string? CurrencyCode, long? TaxCodeId);

/// <summary>A purchase order to insert or update: header fields, computed totals, status and revision.</summary>
public sealed record PurchaseOrderWrite(
    PurchaseOrderHeader Header, PurchaseOrderTotals Totals, IReadOnlyList<PurchaseOrderLine> Lines, PoStatus Status, int RevisionNo);

/// <summary>Purchase orders (MM-UC-003). Callers pass their own transaction.</summary>
public interface IPurchaseOrderRepository
{
    /// <summary>Newest first (po_dt, po_no), at most <paramref name="limit"/> rows.</summary>
    Task<IReadOnlyList<PurchaseOrderRow>> SearchAsync(
        PurchaseOrderSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="forUpdate">Locks the header row until the transaction ends.</param>
    Task<PurchaseOrderRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Lines in seq order.</summary>
    Task<IReadOnlyList<PurchaseOrderLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<PurchaseOrderVendorRow?> FindVendorAsync(long vendorId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>bc_item.item_type of the items that exist.</summary>
    Task<IReadOnlyDictionary<long, string>> FindItemTypesAsync(
        IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The warehouses among <paramref name="warehouseIds"/> that are in use.</summary>
    Task<IReadOnlySet<long>> FindUsableWarehousesAsync(
        IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>The new id.</returns>
    /// <param name="businessPlaceId">bc_business_place.id of the order (BC-BR-321). It does not change after creation.</param>
    Task<long> InsertAsync(
        string poNo, long businessPlaceId, PurchaseOrderWrite order, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Replaces the header and the lines: lines with an Id are updated, others added, missing ones deleted.</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> UpdateAsync(
        long id, PurchaseOrderWrite order, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Sets the status and its audit pair (approved_*, closed_* or cancelled_*).</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> SetStatusAsync(
        long id, PoStatus status, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the order and its lines.</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken);
}

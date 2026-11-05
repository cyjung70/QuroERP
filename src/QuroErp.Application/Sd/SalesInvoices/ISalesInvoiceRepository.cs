using System.Data;
using QuroErp.Contracts.Sd.SalesInvoices;
using QuroErp.Domain.Sd.SalesInvoices;
using QuroErp.Domain.Sd.SalesOrders;
using QuroErp.Domain.Sd.Shipments;

namespace QuroErp.Application.Sd.SalesInvoices;

/// <summary>
/// sd_sales_invoice row of type IV (SD-BR-120) with the customer name, the first order's number and the SHIP_AND_BILL shipment that
/// made it. ShipTo carries the freight charge and fax as the order's does (SD-BR-009, SD-BR-117).
/// </summary>
public sealed record SalesInvoiceRow(
    long Id, string DnNo, string Status, string BillingFlow, long CustomerId, string? CustomerName, long? SalesOrderId, string? SoNo,
    DateOnly DnDt, DateOnly? DueDt, long WarehouseId, long? BusinessPlaceId, string CurrencyCode, decimal ExchangeRate, string? Rep,
    string? Terms, string? ShipVia, SalesOrderShipTo ShipTo, string? Remarks, string? TaxExemptionCertNo, decimal SupplyAmt, decimal TaxAmt,
    decimal TotDnAmt, decimal TotBaseAmt, decimal BalAmt, long? ShipmentId, string? ShipNo, DateTimeOffset UpdatedAt, DateTimeOffset? CreatedAt,
    string? CreatedBy, string? UpdatedBy);

/// <summary>sd_sales_invoice_line row with item, warehouse, lot and the source order or shipment line.</summary>
public sealed record SalesInvoiceLineRow(
    long Id, int Seq, long ItemId, string? ItemCd, string? ItemName, string? Unit, long WarehouseId, string? WarehouseCode, long? LotId,
    string? LotNo, decimal DnQty, decimal DnPrc, decimal DnAmt, string? DcCd, decimal DcRate, decimal? StdPrc, long TaxCodeId, decimal SupplyAmt,
    decimal TaxAmt, long? SalesOrderLineId, string? SoNo, int? SoSeq, long? ShipmentLineId, string? ShipNo, int? ShipSeq, decimal ShippedQty,
    decimal CancelledQty)
{
    public StoredInvoiceLine ToStored() => new(
        Id, Seq, ItemId, ItemName, DnQty, DnPrc, DcCd, DcRate, TaxCodeId, WarehouseId, LotId, ShipmentLineId, SalesOrderLineId, ShippedQty,
        CancelledQty);
}

/// <summary>Header fields a save writes. SalesOrderId is the first order the lines come from.</summary>
public sealed record SalesInvoiceHeader(
    long CustomerId, long? SalesOrderId, DateOnly DnDt, DateOnly DueDt, string CurrencyCode, decimal ExchangeRate, long WarehouseId,
    string BillingFlow, string? Rep, string? Terms, string? ShipVia, SalesOrderShipTo ShipTo, string? Remarks, string? TaxExemptionCertNo);

/// <summary>A new invoice line with its amounts.</summary>
public sealed record SalesInvoiceLineWrite(SalesInvoiceLine Line, decimal DnAmt, decimal SupplyAmt, decimal TaxAmt);

/// <summary>Billed and shipped qty of a shipment's lines, for its status (SD-BR-166).</summary>
public sealed record ShipmentBillingRow(long ShipmentId, string Status, decimal ShipQty, decimal BilledQty);

/// <summary>A Credit Memo an unshipped cancellation made: header and its lines' cancelled invoice line and qty (positive).</summary>
public sealed record CancellationRow(long Id, string CmNo, DateOnly CmDt, decimal TotDnAmt, decimal BalAmt);

/// <summary>A Credit Memo line of a cancellation: its seq, the invoice line it cancels and the qty (positive).</summary>
public sealed record CancellationLineRow(int Seq, long CancelledInvoiceLineId, decimal Qty);

/// <summary>Header of the Credit Memo an unshipped cancellation makes (SD-BR-145, SD-BR-231).</summary>
public sealed record CreditMemoHeader(
    long CustomerId, DateOnly CmDt, string CurrencyCode, decimal ExchangeRate, long WarehouseId, long? BusinessPlaceId,
    IReadOnlyList<bool> Reasons, decimal SupplyAmt, decimal TaxAmt, decimal TotDnAmt, decimal TotBaseAmt, string? Remarks);

/// <summary>A Credit Memo line: negative qty and amounts, the invoice line it cancels.</summary>
public sealed record CreditMemoLineWrite(
    int Seq, long ItemId, string? ItemName, decimal DnQty, decimal DnPrc, decimal DnAmt, long WarehouseId, long TaxCodeId, decimal SupplyAmt,
    decimal TaxAmt, long? SalesOrderLineId, long? LotId, long CancelledInvoiceLineId);

/// <summary>Receipts and Credit Memos applied to an invoice (SD-BR-104).</summary>
public sealed record SalesInvoiceApplied(decimal PaymentApplied, decimal CreditApplied);

/// <summary>A line of the sales invoice print and its tax kind.</summary>
public sealed record SalesInvoiceTaxLine(int Seq, string TaxKind, decimal TaxRate);

/// <summary>Sales invoices (SD-UC-003). Every query reads type IV only (SD-BR-120). Callers pass their own transaction.</summary>
public interface ISalesInvoiceRepository
{
    /// <summary>Newest first (dn_dt, dn_no), at most <paramref name="limit"/> rows.</summary>
    Task<IReadOnlyList<SalesInvoiceRow>> SearchAsync(SalesInvoiceSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="forUpdate">Locks the header row until the transaction ends.</param>
    Task<SalesInvoiceRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Lines in seq order.</summary>
    Task<IReadOnlyList<SalesInvoiceLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Locks the shipments of these lines (in id order) and reads the lines with their shipment.</summary>
    Task<IReadOnlyList<InvoiceShipmentSource>> LockShipmentLinesAsync(
        IReadOnlyCollection<long> shipmentLineIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Locks the orders of these lines (in id order) and reads the lines with their order.</summary>
    Task<IReadOnlyList<InvoiceOrderSource>> LockOrderLinesAsync(
        IReadOnlyCollection<long> salesOrderLineIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Inserts the header (amounts 0, status OP) and lines.</summary>
    /// <returns>The new id and the new line ids by seq.</returns>
    Task<(long Id, IReadOnlyDictionary<int, long> LineIds)> InsertAsync(
        string dnNo, long businessPlaceId, SalesInvoiceHeader header, IReadOnlyList<SalesInvoiceLineWrite> lines, string user,
        IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Writes the header fields, deletes these lines and adds the new ones.</summary>
    /// <returns>The new line ids by seq, or null when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<IReadOnlyDictionary<int, long>?> UpdateAsync(
        long id, SalesInvoiceHeader header, IReadOnlyCollection<long> deletedLineIds, IReadOnlyList<SalesInvoiceLineWrite> added,
        DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Writes every line's amounts by seq and the totals with balance = total and status OP (SD-BR-109).</summary>
    Task UpdateAmountsAsync(
        long id, IReadOnlyList<ShipmentInvoiceLineAmounts> amounts, ShipmentInvoiceTotals totals, string user, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>Sets the lot of the lines by seq (SD-BR-142).</summary>
    Task SetLineLotsAsync(long id, IReadOnlyDictionary<int, long> lotIdBySeq, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the invoice and its lines.</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Voids the invoice: line qty and amounts 0, header VO with total and balance 0 (SD-BR-113).</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> VoidAsync(long id, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Marks the invoice changed (updated_at, updated_by), for a cancellation or its undo.</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> TouchAsync(long id, DateTimeOffset? updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Adds to sd_shipment_line.billed_qty (SD-BR-108); a negative qty takes back.</summary>
    Task AddShipmentBilledQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByShipmentLine, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Adds to sd_sales_invoice_line.cancelled_qty (SD-BR-145); a negative qty takes back (SD-BR-146).</summary>
    Task AddCancelledQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByInvoiceLine, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Line qty and status of these shipments.</summary>
    Task<IReadOnlyList<ShipmentBillingRow>> FindShipmentBillingAsync(
        IReadOnlyCollection<long> shipmentIds, IDbTransaction transaction, CancellationToken cancellationToken);

    Task SetShipmentStatusAsync(long shipmentId, string status, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>TR movements with this ref key and these ref seqs that are neither reversals nor reversed, in id order.</summary>
    Task<IReadOnlyList<long>> FindOpenTransferIdsAsync(
        string refKey, IReadOnlyCollection<int> seqs, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Receipts and Credit Memos applied to the invoice.</summary>
    Task<SalesInvoiceApplied> FindAppliedAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>SD-BR-118: − the sum of the customer's Credit Memo balances.</summary>
    Task<decimal> FindAvailableCreditAsync(long customerId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The Credit Memos whose lines cancel lines of this invoice (SD-BR-145), in number order.</summary>
    Task<IReadOnlyList<CancellationRow>> FindCancellationsAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// One Credit Memo of a cancellation of this invoice, or null when it is not one. forUpdate locks the Credit Memo row.
    /// </summary>
    Task<(CancellationRow Header, IReadOnlyList<CancellationLineRow> Lines)?> FindCancellationAsync(
        long id, long creditMemoId, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Inserts a Credit Memo (type CM, status OP, balance = total, restock false) and its lines.</summary>
    /// <returns>The new id.</returns>
    Task<long> InsertCreditMemoAsync(
        string cmNo, CreditMemoHeader header, IReadOnlyList<CreditMemoLineWrite> lines, string user, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>Deletes a Credit Memo and its lines.</summary>
    Task DeleteCreditMemoAsync(long creditMemoId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// SD-BR-141: lines of the customer's BILL_THEN_SHIP orders in this currency that are not closed and have qty left to bill, with
    /// the item's stock outside the consignment warehouse.
    /// </summary>
    Task<IReadOnlyList<OpenBillFirstOrderLineDto>> FindOpenOrderLinesAsync(
        long customerId, string currencyCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Tax kind and rate of each line's tax code, in seq order (SD-BR-133, SD-BR-134).</summary>
    Task<IReadOnlyList<SalesInvoiceTaxLine>> FindTaxLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);
}

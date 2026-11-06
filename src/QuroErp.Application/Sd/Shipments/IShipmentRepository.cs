using System.Data;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Domain.Sd.Shipments;

namespace QuroErp.Application.Sd.Shipments;

/// <summary>Ship-to of a shipment and its SHIP_AND_BILL invoice, copied from the order (SD-BR-172).</summary>
public sealed record ShipmentShipTo(
    string? ShipName, string? ShipAddr1, string? ShipAddr2, string? ShipCity, string? ShipState, string? ShipZipCode, string? ShipCountry,
    string? ShipPhone);

/// <summary>sd_shipment row with the customer name, order number and the SHIP_AND_BILL invoice number.</summary>
public sealed record ShipmentRow(
    long Id, string ShipNo, string Status, string BillingFlow, long CustomerId, string? CustomerName, long? SalesOrderId, string? SoNo,
    DateOnly ShipDt, long WarehouseId, string CurrencyCode, decimal ExchangeRate, string? ShipVia, ShipmentShipTo ShipTo, string? Remarks,
    long? SalesInvoiceId, string? SalesInvoiceNo, DateTimeOffset UpdatedAt, DateTimeOffset? CreatedAt, string? CreatedBy, string? UpdatedBy);

/// <summary>sd_shipment_line row with item, warehouse, lot, on-hand qty now and the source order or invoice line.</summary>
public sealed record ShipmentLineRow(
    long Id, int Seq, long ItemId, string? ItemCd, string? ItemName, string? Unit, long WarehouseId, string? WarehouseCode, long? LotId,
    string? LotNo, decimal ShipQty, decimal ShipPrc, string? DcCd, decimal DcRate, long TaxCodeId, decimal BilledQty, decimal OnhandQty,
    long? SalesOrderLineId, string? SoNo, int? SoSeq, long? SalesInvoiceLineId, string? DnNo, int? DnSeq, decimal? RemainingQty)
{
    public StoredShipmentLine ToStored() => new(
        Id, Seq, ItemId, ItemName, WarehouseId, ShipQty, ShipPrc, DcCd, DcRate, TaxCodeId, LotId, BilledQty, SalesOrderLineId, SalesInvoiceLineId);
}

/// <summary>What the amount of a listed shipment is calculated from.</summary>
public sealed record ShipmentAmountLineRow(long ShipmentId, int Seq, decimal ShipQty, decimal ShipPrc, decimal DcRate, long TaxCodeId);

/// <summary>Header fields a save writes. SalesOrderId is the first order the lines come from.</summary>
public sealed record ShipmentHeader(
    long CustomerId, long? SalesOrderId, DateOnly ShipDt, long WarehouseId, string CurrencyCode, decimal ExchangeRate, string BillingFlow,
    string? ShipVia, ShipmentShipTo ShipTo, string? Remarks);

/// <summary>The order header values a SHIP_AND_BILL invoice copies (SD-BR-167).</summary>
public sealed record ShipmentOrderTerms(string? Rep, string? Terms, DateOnly? DueDt, string? FreightCharge);

/// <summary>The SHIP_AND_BILL invoice of a shipment: number, amounts and status (SD-BR-104).</summary>
public sealed record ShipmentInvoiceRow(long Id, string DnNo, decimal TotDnAmt, decimal BalAmt, string? Status);

/// <summary>Header of the SHIP_AND_BILL invoice: the shipment's date, ship-to, currency and warehouse and the order's terms.</summary>
public sealed record ShipmentInvoiceHeader(
    long CustomerId, long? SalesOrderId, DateOnly DnDt, DateOnly DueDt, string? Terms, string? Rep, string? ShipVia, string? FreightCharge,
    ShipmentShipTo ShipTo, string CurrencyCode, decimal ExchangeRate, long WarehouseId, string? Remarks);

/// <summary>A SHIP_AND_BILL invoice line to insert: one shipment line with its amounts (SD-BR-167).</summary>
public sealed record ShipmentInvoiceLineWrite(
    int Seq, long ItemId, string? ItemName, decimal DnQty, decimal DnPrc, decimal DnAmt, long? SalesOrderLineId, string? DcCd, decimal DcRate,
    long WarehouseId, long TaxCodeId, decimal SupplyAmt, decimal TaxAmt, long ShipmentLineId);

/// <summary>Quantities of the lines of an order, for its status (SD-BR-012).</summary>
public sealed record ShipmentOrderQuantity(long SalesOrderId, string Status, decimal SoQty, decimal ShipQty);

/// <summary>An open order (SD-BR-172) with one of its lines left to ship; the service groups them per order.</summary>
public sealed record OpenOrderLineRow(
    long SalesOrderId, string SoNo, DateOnly SoDt, string CurrencyCode, decimal ExchangeRate, string BillingFlow, string? Rep, string? ShipVia,
    string? ShipName, string? ShipAddr1, string? ShipAddr2, string? ShipCity, string? ShipState, string? ShipZipCode, string? ShipCountry,
    string? ShipPhone, long SalesOrderLineId, int Seq, long ItemId, string? ItemCd, string? ItemName, string? Unit, decimal SoQty, decimal ShipQty,
    decimal SoPrc, string? DcCd, decimal DcRate, long TaxCodeId);

/// <summary>A delivery note line: the item, its location and the shipped qty.</summary>
public sealed record ShipmentPrintLine(int Seq, string? ItemCd, string? ItemName, string? Unit, string? WarehouseCode, string? LotNo, decimal ShipQty);

/// <summary>Shipments (SD-UC-051). Callers pass their own transaction.</summary>
public interface IShipmentRepository
{
    /// <summary>Newest first (ship_dt, ship_no), at most <paramref name="limit"/> rows.</summary>
    Task<IReadOnlyList<ShipmentRow>> SearchAsync(ShipmentSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The lines of these shipments, by shipment and seq, for the list's amounts.</summary>
    Task<IReadOnlyList<ShipmentAmountLineRow>> FindAmountLinesAsync(IReadOnlyList<long> shipmentIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="forUpdate">Locks the header row until the transaction ends.</param>
    Task<ShipmentRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Lines in seq order.</summary>
    Task<IReadOnlyList<ShipmentLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Locks the orders of these lines (in id order) and reads the lines with their order.</summary>
    Task<IReadOnlyList<ShipmentOrderLine>> LockOrderLinesAsync(
        IReadOnlyCollection<long> salesOrderLineIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Locks the invoices of these lines (in id order) and reads the lines with their invoice.</summary>
    Task<IReadOnlyList<ShipmentInvoiceLine>> LockInvoiceLinesAsync(
        IReadOnlyCollection<long> salesInvoiceLineIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Rep, terms, due date and freight of the order, or null when it does not exist.</summary>
    Task<ShipmentOrderTerms?> FindOrderTermsAsync(long salesOrderId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Item codes of the items with these ids that exist.</summary>
    Task<IReadOnlyDictionary<long, string>> FindItemCodesAsync(
        IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The ids among these that are warehouses in use (MM-BR-704).</summary>
    Task<IReadOnlySet<long>> FindUsableWarehousesAsync(
        IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>MM-BR-703: the user's default warehouse when in use and not the consignment warehouse, else the company default.</summary>
    Task<long?> FindDefaultWarehouseAsync(string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// DN movements of these lines that are neither reversals nor reversed, in id order. RefKeys are the shipment number and, for a
    /// migrated shipment, the invoice number its movements were posted under.
    /// </summary>
    Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(
        IReadOnlyCollection<string> refKeys, IReadOnlyCollection<int> seqs, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Inserts the header and lines.</summary>
    /// <param name="businessPlaceId">bc_business_place.id of the shipment (BC-BR-321).</param>
    /// <returns>The new id and the new line ids by seq.</returns>
    Task<(long Id, IReadOnlyDictionary<int, long> LineIds)> InsertAsync(
        string shipNo, long businessPlaceId, ShipmentHeader header, string status, IReadOnlyList<ShipmentLine> lines, string user,
        IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the header fields a save can change, deletes these lines (with the SHIP_AND_BILL invoice lines that bill them,
    /// SD-BR-167) and adds the new ones.
    /// </summary>
    /// <returns>The new line ids by seq, or null when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<IReadOnlyDictionary<int, long>?> UpdateAsync(
        long id, ShipmentHeader header, IReadOnlyCollection<long> deletedLineIds, IReadOnlyList<ShipmentLine> added, DateTimeOffset updatedAt,
        string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Sets the lot of the lines by seq (MM-BR-355).</summary>
    Task SetLineLotsAsync(long id, IReadOnlyDictionary<int, long> lotIdBySeq, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Sets the status and the SHIP_AND_BILL invoice of the shipment.</summary>
    Task SetStatusAsync(long id, string status, long? salesInvoiceId, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Voids the shipment: status VO, billed qty of the lines back to 0 (SD-BR-169).</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> VoidAsync(long id, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Adds to sd_sales_order_line.ship_qty (SD-BR-165); a negative qty takes back.</summary>
    Task AddOrderShipQtyAsync(IReadOnlyDictionary<long, decimal> qtyByOrderLine, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Adds to sd_sales_order_line.dn_qty (SD-BR-108); a negative qty takes back.</summary>
    Task AddOrderDnQtyAsync(IReadOnlyDictionary<long, decimal> qtyByOrderLine, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Adds to sd_sales_invoice_line.shipped_qty (SD-BR-176); a negative qty takes back (SD-BR-177).</summary>
    Task AddInvoiceShippedQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByInvoiceLine, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Line quantities and status of these orders.</summary>
    Task<IReadOnlyList<ShipmentOrderQuantity>> FindOrderQuantitiesAsync(
        IReadOnlyCollection<long> salesOrderIds, IDbTransaction transaction, CancellationToken cancellationToken);

    Task SetOrderStatusAsync(long salesOrderId, string status, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="forUpdate">Locks the invoice row until the transaction ends.</param>
    Task<ShipmentInvoiceRow?> FindInvoiceAsync(long salesInvoiceId, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Inserts the SHIP_AND_BILL invoice header with zero amounts (SD-BR-106, SD-BR-167).</summary>
    /// <returns>The new id.</returns>
    Task<long> InsertInvoiceAsync(
        string dnNo, long businessPlaceId, ShipmentInvoiceHeader header, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the SHIP_AND_BILL invoice in line with its shipment: adds the new lines, then writes every line's amounts, the header
    /// copy fields and the totals with balance = total and status OP (SD-BR-109). Lines of deleted shipment lines went with them.
    /// </summary>
    Task UpdateInvoiceAsync(
        long salesInvoiceId, ShipmentInvoiceHeader header, IReadOnlyList<ShipmentInvoiceLineWrite> added,
        IReadOnlyList<ShipmentInvoiceLineAmounts> amounts, ShipmentInvoiceTotals totals, string user, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>
    /// Voids the SHIP_AND_BILL invoice: line qty and amounts 0, header VO with total and balance 0 (SD-BR-113, SD-BR-169).
    /// </summary>
    Task VoidInvoiceAsync(long salesInvoiceId, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Lines of the customer's open orders left to ship, but not of BILL_THEN_SHIP orders (SD-BR-172).</summary>
    Task<IReadOnlyList<OpenOrderLineRow>> FindOpenOrderLinesAsync(long customerId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>SHIP_THEN_BILL lines of the customer in this currency left to bill, of shipments not void (SD-BR-115).</summary>
    Task<IReadOnlyList<UnbilledShipmentLineDto>> FindUnbilledLinesAsync(
        long customerId, string currencyCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>BILL_THEN_SHIP invoice lines of the customer left to ship, of invoices not void (SD-BR-175).</summary>
    Task<IReadOnlyList<UnshippedInvoiceLineDto>> FindUnshippedInvoiceLinesAsync(
        long customerId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Lines of the delivery note in seq order.</summary>
    Task<IReadOnlyList<ShipmentPrintLine>> FindPrintLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);
}

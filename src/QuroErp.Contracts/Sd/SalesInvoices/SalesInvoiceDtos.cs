namespace QuroErp.Contracts.Sd.SalesInvoices;

/// <summary>Sales invoice API routes (SD-UC-003).</summary>
public static class SalesInvoiceRoutes
{
    /// <summary>GET list (query <see cref="SalesInvoiceSearchQuery"/>), POST new invoice; GET, PUT, DELETE {Base}/{id}.</summary>
    public const string Base = "api/sd/sales-invoices";

    /// <summary>POST {Base}/{id}/void with <see cref="VoidSalesInvoiceRequest"/> (SD-BR-113).</summary>
    public const string Void = "void";

    /// <summary>GET {Base}/open-order-lines?customerId&amp;currencyCode: BILL_THEN_SHIP order lines left to bill (SD-BR-141).</summary>
    public const string OpenOrderLines = "open-order-lines";

    /// <summary>POST {Base}/{id}/cancel-unshipped with <see cref="CancelUnshippedRequest"/> (SD-BR-145).</summary>
    public const string CancelUnshipped = "cancel-unshipped";

    /// <summary>DELETE {Base}/{id}/cancellations/{creditMemoId}: undoes a cancellation (SD-BR-146).</summary>
    public const string Cancellations = "cancellations";

    /// <summary>GET {Base}/{id}/print/sales-invoice.pdf.</summary>
    public const string PrintInvoice = "print/sales-invoice.pdf";

    /// <summary>GET {Base}/{id}/print/tax-invoice.pdf: the KR tax invoice (SD-BR-134).</summary>
    public const string PrintTaxInvoice = "print/tax-invoice.pdf";

    /// <summary>Most rows the list returns, newest first.</summary>
    public const int MaxListSize = 200;
}

/// <summary>List conditions; all optional. DnNo matches the start of the number.</summary>
public sealed record SalesInvoiceSearchQuery(string? DnNo, long? CustomerId, string? Status, DateOnly? FromDt, DateOnly? ToDt);

public sealed record SalesInvoiceSummaryDto(
    long Id, string DnNo, string Status, string BillingFlow, DateOnly DnDt, long CustomerId, string? CustomerName, string CurrencyCode,
    decimal TotDnAmt, decimal BalAmt);

/// <summary>
/// One invoice. Status is OP, PA, CL or VO (SD-BR-105). BillingFlow tells how it bills: SHIP_AND_BILL (made by ShipmentId, whose
/// lines change on the shipment, SD-BR-136), SHIP_THEN_BILL (shipment lines, SD-BR-115) or BILL_THEN_SHIP (order lines billed
/// before shipping, SD-BR-141). PaymentApplied and CreditApplied are what receipts and Credit Memos took off the total; AvailableCredit
/// is the customer's unapplied Credit Memo balance (SD-BR-118). Cancellations are the Credit Memos of unshipped cancellations
/// (SD-BR-145). TaxExemptionCertNo is the US exemption certificate the invoice was exempted by (SD-BR-133).
/// </summary>
public sealed record SalesInvoiceDto(
    long Id,
    string DnNo,
    string Status,
    string BillingFlow,
    long CustomerId,
    string? CustomerName,
    long? SalesOrderId,
    string? SoNo,
    DateOnly DnDt,
    DateOnly? DueDt,
    long WarehouseId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Rep,
    string? Terms,
    string? ShipVia,
    string? FreightCharge,
    string? ShipName,
    string? ShipAddr1,
    string? ShipAddr2,
    string? ShipCity,
    string? ShipState,
    string? ShipZipCode,
    string? ShipCountry,
    string? ShipPhone,
    string? ShipFax,
    string? Remarks,
    string? TaxExemptionCertNo,
    decimal SupplyAmt,
    decimal TaxAmt,
    decimal TotDnAmt,
    decimal TotBaseAmt,
    decimal BalAmt,
    decimal PaymentApplied,
    decimal CreditApplied,
    decimal AvailableCredit,
    long? ShipmentId,
    string? ShipNo,
    IReadOnlyList<SalesInvoiceLineDto> Lines,
    IReadOnlyList<SalesInvoiceCancellationDto> Cancellations,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CreatedAt = null,
    string? CreatedBy = null,
    string? UpdatedBy = null);

/// <summary>
/// One line. ShipNo/ShipSeq name the shipment line it bills, SoNo/SoSeq the order line. ShippedQty, CancelledQty and UnshippedQty
/// are a BILL_THEN_SHIP line's progress (SD-BR-143, SD-BR-147). StdPrc is the item's standard sales price.
/// </summary>
public sealed record SalesInvoiceLineDto(
    long Id,
    int Seq,
    long ItemId,
    string? ItemCd,
    string? ItemName,
    string? Unit,
    long WarehouseId,
    string? WarehouseCode,
    long? LotId,
    string? LotNo,
    decimal DnQty,
    decimal DnPrc,
    decimal DnAmt,
    string? DcCd,
    decimal DcRate,
    decimal? StdPrc,
    long TaxCodeId,
    decimal SupplyAmt,
    decimal TaxAmt,
    long? SalesOrderLineId,
    string? SoNo,
    int? SoSeq,
    long? ShipmentLineId,
    string? ShipNo,
    int? ShipSeq,
    decimal ShippedQty,
    decimal CancelledQty,
    decimal UnshippedQty);

/// <summary>A Credit Memo an unshipped cancellation made (SD-BR-145): its amounts are negative, Applied when total ≠ balance.</summary>
public sealed record SalesInvoiceCancellationDto(long CreditMemoId, string CmNo, DateOnly CmDt, decimal TotDnAmt, decimal BalAmt, bool Applied);

/// <summary>
/// New or changed invoice. Lines are the whole list: saved lines come back unchanged with their Id (SD-BR-107), lines left out
/// are deleted and lines without Id are added. CurrencyCode and ExchangeRate apply only to an invoice of direct lines (default: the
/// customer's currency and the rate of the invoice date); shipments and orders bring theirs (SD-BR-131). WarehouseId is the
/// default of BILL_THEN_SHIP and direct lines (MM-BR-703). The customer (SD-BR-119) and DnDt do not change after the first save.
/// UpdatedAt is required on PUT. UpdateCustomerShipTo and ConfirmCreditLimit answer SD-W-009 and SD-W-309 (SD-BR-117, SD-BR-309):
/// null asks with 422 and confirmField. TaxExemptionCertNo is the certificate the user entered; null keeps the customer's (SD-BR-133).
/// </summary>
public sealed record SaveSalesInvoiceRequest(
    long CustomerId,
    DateOnly DnDt,
    DateOnly? DueDt,
    string? CurrencyCode,
    decimal? ExchangeRate,
    long? WarehouseId,
    string? Rep,
    string? Terms,
    string? ShipVia,
    string? FreightCharge,
    string? ShipName,
    string? ShipAddr1,
    string? ShipAddr2,
    string? ShipCity,
    string? ShipState,
    string? ShipZipCode,
    string? ShipCountry,
    string? ShipPhone,
    string? ShipFax,
    string? Remarks,
    IReadOnlyList<SaveSalesInvoiceLineRequest> Lines,
    DateTimeOffset? UpdatedAt,
    bool? UpdateCustomerShipTo = null,
    bool? ConfirmCreditLimit = null,
    string? TaxExemptionCertNo = null);

/// <summary>
/// One line. ShipmentLineId bills a SHIP_THEN_BILL shipment line (SD-BR-115), SalesOrderLineId a BILL_THEN_SHIP order line
/// (SD-BR-141): their item, price and discount come from the source. Neither is a direct line of an item that keeps no stock, whose
/// DnPrc is required (SD-BR-116). TaxCodeId defaults to the source's or the customer's (SD-BR-132). LotId or LotNo picks the lot a
/// lot-managed BILL_THEN_SHIP line moves (SD-BR-142).
/// </summary>
public sealed record SaveSalesInvoiceLineRequest(
    long? Id, long ItemId, string? ItemName, decimal DnQty, decimal? DnPrc, string? DcCd, long? TaxCodeId, long? WarehouseId, long? LotId,
    string? LotNo, long? ShipmentLineId, long? SalesOrderLineId);

/// <summary>Void or delete with the updatedAt the screen read.</summary>
public sealed record VoidSalesInvoiceRequest(DateTimeOffset UpdatedAt);

/// <summary>
/// Unshipped cancellation (SD-BR-145): the qty to cancel per BILL_THEN_SHIP line, the Credit Memo date and its reasons
/// (cm_reason01..08 in order, SD-BR-208; missing values are false).
/// </summary>
public sealed record CancelUnshippedRequest(
    DateOnly CancelDt, IReadOnlyList<bool>? Reasons, IReadOnlyList<CancelUnshippedLineRequest> Lines, DateTimeOffset UpdatedAt);

public sealed record CancelUnshippedLineRequest(long SalesInvoiceLineId, decimal Qty);

/// <summary>The Credit Memo an unshipped cancellation made.</summary>
public sealed record CancelUnshippedResult(long CreditMemoId, string CmNo);

/// <summary>
/// A BILL_THEN_SHIP order line left to bill (SD-BR-141), with the header values an invoice copies. RemainingQty = SoQty − DnQty is
/// the default qty. SellableQty is the item's stock outside the consignment warehouse (MM-BR-709).
/// </summary>
public sealed record OpenBillFirstOrderLineDto(
    long SalesOrderId, string SoNo, DateOnly SoDt, string CurrencyCode, decimal ExchangeRate, string? Rep, string? Terms, DateOnly? DueDt,
    string? ShipVia, string? FreightCharge, string? ShipName, string? ShipAddr1, string? ShipAddr2, string? ShipCity, string? ShipState,
    string? ShipZipCode, string? ShipCountry, string? ShipPhone, long SalesOrderLineId, int Seq, long ItemId, string? ItemCd, string? ItemName,
    string? Unit, decimal SoQty, decimal DnQty, decimal RemainingQty, decimal SoPrc, string? DcCd, decimal DcRate, long TaxCodeId,
    decimal SellableQty);

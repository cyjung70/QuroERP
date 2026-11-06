namespace QuroErp.Contracts.Sd.Shipments;

/// <summary>Shipment API routes (SD-UC-051).</summary>
public static class ShipmentRoutes
{
    /// <summary>GET list (query <see cref="ShipmentSearchQuery"/>), POST new shipment; GET, PUT {Base}/{id}; POST {Base}/{id}/void.</summary>
    public const string Base = "api/sd/shipments";

    /// <summary>POST {Base}/{id}/void with <see cref="VoidShipmentRequest"/> (SD-BR-169).</summary>
    public const string Void = "void";

    /// <summary>GET {Base}/open-order-lines?customerId: order lines left to ship (SD-BR-172).</summary>
    public const string OpenOrderLines = "open-order-lines";

    /// <summary>GET {Base}/unbilled-lines?customerId&amp;currencyCode: SHIP_THEN_BILL lines left to bill (SD-BR-115).</summary>
    public const string UnbilledLines = "unbilled-lines";

    /// <summary>GET {Base}/unshipped-invoice-lines?customerId: BILL_THEN_SHIP invoice lines left to ship (SD-BR-175).</summary>
    public const string UnshippedInvoiceLines = "unshipped-invoice-lines";

    /// <summary>GET {Base}/{id}/print.pdf: the delivery note.</summary>
    public const string Print = "print.pdf";

    /// <summary>Most rows the list returns, newest first.</summary>
    public const int MaxListSize = 200;
}

/// <summary>List conditions; all optional. ShipNo matches the start of the number, CustomerName any part of the name.</summary>
public sealed record ShipmentSearchQuery(
    string? ShipNo, long? CustomerId, string? Status, DateOnly? FromDt, DateOnly? ToDt, string? CustomerName = null);

/// <summary>A row of the list. ShipAmt is calculated, not stored: the line amounts after discount without tax, in the document currency.</summary>
public sealed record ShipmentSummaryDto(
    long Id, string ShipNo, string Status, string BillingFlow, DateOnly ShipDt, long CustomerId, string? CustomerName, string CurrencyCode,
    string? SalesInvoiceNo, decimal ShipAmt);

/// <summary>
/// One shipment. Status is OP, PB, BL or VO (SD-BR-166). BillingFlow is fixed by the first save (SD-BR-174). SalesInvoiceId and
/// SalesInvoiceNo are the invoice a SHIP_AND_BILL shipment made (SD-BR-167). Currency and rate come from the order or invoice
/// (SD-BR-170). CreatedAt, CreatedBy and UpdatedBy are the audit line of the screen. InvoiceHasPayments says a SHIP_AND_BILL invoice
/// has a receipt or credit applied, so the shipment no longer changes (SD-BR-104, SD-BR-167).
/// </summary>
public sealed record ShipmentDto(
    long Id,
    string ShipNo,
    string Status,
    string BillingFlow,
    long CustomerId,
    string? CustomerName,
    long? SalesOrderId,
    string? SoNo,
    DateOnly ShipDt,
    long WarehouseId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? ShipVia,
    string? ShipName,
    string? ShipAddr1,
    string? ShipAddr2,
    string? ShipCity,
    string? ShipState,
    string? ShipZipCode,
    string? ShipCountry,
    string? ShipPhone,
    string? Remarks,
    long? SalesInvoiceId,
    string? SalesInvoiceNo,
    IReadOnlyList<ShipmentLineDto> Lines,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CreatedAt = null,
    string? CreatedBy = null,
    string? UpdatedBy = null,
    bool InvoiceHasPayments = false);

/// <summary>
/// One line. ShipPrc, DcCd, DcRate and TaxCodeId come from the order or invoice line (SD-BR-167, SD-BR-175), or as entered for a
/// direct line (SD-BR-173). BilledQty is billed (SD-BR-108). OnhandQty is the line warehouse's on-hand qty now. SoNo/SoSeq and
/// DnNo/DnSeq name the source line. RemainingQty is what the source line still has to ship; null for a direct line.
/// </summary>
public sealed record ShipmentLineDto(
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
    decimal ShipQty,
    decimal ShipPrc,
    string? DcCd,
    decimal DcRate,
    long TaxCodeId,
    decimal BilledQty,
    decimal OnhandQty,
    long? SalesOrderLineId,
    string? SoNo,
    int? SoSeq,
    long? SalesInvoiceLineId,
    string? DnNo,
    int? DnSeq,
    decimal? RemainingQty = null);

/// <summary>
/// New or changed shipment. Lines are the whole list: saved lines come back unchanged with their Id (SD-BR-171), lines left out
/// are deleted and lines without Id are added. WarehouseId defaults to the user's default warehouse (MM-BR-703). CurrencyCode and
/// ExchangeRate apply only to a shipment of direct lines (default: the customer's currency and the latest rate, BC-BR-412); order
/// and invoice lines bring theirs (SD-BR-170). ShipDt and the customer do not change after the first save. UpdatedAt is required
/// on PUT. ConfirmCreditLimit answers SD-W-309 (SD-BR-309): null asks with 422 and confirmField, true saves.
/// </summary>
public sealed record SaveShipmentRequest(
    long CustomerId,
    DateOnly ShipDt,
    long? WarehouseId,
    string? CurrencyCode,
    decimal? ExchangeRate,
    string? ShipVia,
    string? ShipName,
    string? ShipAddr1,
    string? ShipAddr2,
    string? ShipCity,
    string? ShipState,
    string? ShipZipCode,
    string? ShipCountry,
    string? ShipPhone,
    string? Remarks,
    IReadOnlyList<SaveShipmentLineRequest> Lines,
    DateTimeOffset? UpdatedAt,
    bool? ConfirmCreditLimit = null);

/// <summary>
/// One line. SalesOrderLineId ships an order line (SD-BR-172), SalesInvoiceLineId a BILL_THEN_SHIP invoice line (SD-BR-175);
/// neither is a direct line, whose ShipPrc and TaxCodeId are required (SD-BR-173). WarehouseId defaults to the header warehouse.
/// LotId or LotNo picks the lot of a lot-managed item (MM-BR-355).
/// </summary>
public sealed record SaveShipmentLineRequest(
    long? Id, long ItemId, string? ItemName, long? WarehouseId, decimal ShipQty, decimal? ShipPrc, long? TaxCodeId, long? LotId, string? LotNo,
    long? SalesOrderLineId, long? SalesInvoiceLineId);

/// <summary>Void with the updatedAt the screen read.</summary>
public sealed record VoidShipmentRequest(DateTimeOffset UpdatedAt);

/// <summary>
/// An order the shipment can import (SD-BR-172) with the header values a shipment copies and its lines left to ship.
/// </summary>
public sealed record OpenSalesOrderDto(
    long SalesOrderId,
    string SoNo,
    DateOnly SoDt,
    string CurrencyCode,
    decimal ExchangeRate,
    string BillingFlow,
    string? Rep,
    string? ShipVia,
    string? ShipName,
    string? ShipAddr1,
    string? ShipAddr2,
    string? ShipCity,
    string? ShipState,
    string? ShipZipCode,
    string? ShipCountry,
    string? ShipPhone,
    IReadOnlyList<OpenSalesOrderLineDto> Lines);

/// <summary>An order line left to ship: RemainingQty = SoQty − ShipQty, the default ship qty (SD-BR-172).</summary>
public sealed record OpenSalesOrderLineDto(
    long SalesOrderLineId, int Seq, long ItemId, string? ItemCd, string? ItemName, string? Unit, decimal SoQty, decimal ShipQty,
    decimal RemainingQty, decimal SoPrc, string? DcCd, decimal DcRate, long TaxCodeId);

/// <summary>A SHIP_THEN_BILL shipment line left to bill (SD-BR-115): UnbilledQty = ShipQty − BilledQty.</summary>
public sealed record UnbilledShipmentLineDto(
    long ShipmentId, string ShipNo, DateOnly ShipDt, long ShipmentLineId, int Seq, long ItemId, string? ItemCd, string? ItemName,
    long WarehouseId, decimal ShipQty, decimal BilledQty, decimal UnbilledQty, decimal ShipPrc, string? DcCd, decimal DcRate,
    long TaxCodeId, long? SalesOrderId, string? SoNo);

/// <summary>
/// A BILL_THEN_SHIP invoice line left to ship (SD-BR-175): UnshippedQty = InvoicedQty − ShippedQty − CancelledQty (SD-BR-143).
/// </summary>
public sealed record UnshippedInvoiceLineDto(
    long SalesInvoiceLineId, long SalesInvoiceId, string DnNo, int Seq, long ItemId, string? ItemCd, string? ItemName, decimal InvoicedQty,
    decimal ShippedQty, decimal CancelledQty, decimal UnshippedQty, decimal Prc, string? DcCd, decimal DcRate, long TaxCodeId,
    string CurrencyCode, decimal ExchangeRate, string? Unit = null);

namespace QuroErp.Contracts.Mm.PurchaseOrders;

/// <summary>Purchase order API routes (MM-UC-003).</summary>
public static class PurchaseOrderRoutes
{
    /// <summary>GET list (query <see cref="PurchaseOrderSearchQuery"/>), POST new order; GET, PUT, DELETE {Base}/{id}.</summary>
    public const string Base = "api/mm/purchase-orders";

    /// <summary>POST {Base}/{id}/approve, close and cancel with <see cref="PurchaseOrderActionRequest"/> (MM-BR-020, 022, 023).</summary>
    public const string Approve = "approve";
    public const string Close = "close";
    public const string Cancel = "cancel";

    /// <summary>Most rows the list returns, newest first.</summary>
    public const int MaxListSize = 200;
}

/// <summary>List and find conditions; all optional. PoNo matches the start of the number.</summary>
public sealed record PurchaseOrderSearchQuery(string? PoNo, long? VendorId, string? Status, DateOnly? FromDt, DateOnly? ToDt);

public sealed record PurchaseOrderSummaryDto(
    long Id, string PoNo, string Status, DateOnly PoDt, long VendorId, string? VendorName, string CurrencyCode, decimal TotPoAmt);

/// <summary>
/// One purchase order. Amounts are computed by the server (MM-BR-015, MM-BR-032): GrossAmt and DcAmt for the totals panel,
/// SupplyAmt + TaxAmt = TotPoAmt, TotBaseAmt in base currency. RevisionNo counts saves after approval (MM-BR-021).
/// ReceivedQty is the quantity received on all lines; anything above 0 blocks edit, delete and cancel (MM-BR-013, 023).
/// </summary>
public sealed record PurchaseOrderDto(
    long Id,
    string PoNo,
    string Status,
    long VendorId,
    string? VendorName,
    DateOnly PoDt,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Incoterms,
    DateOnly? DueDt,
    long? WarehouseId,
    string? PaymentTerms,
    string? Buyer,
    string? Remarks,
    decimal GrossAmt,
    decimal DcAmt,
    decimal SupplyAmt,
    decimal TaxAmt,
    decimal TotPoAmt,
    decimal TotBaseAmt,
    int RevisionNo,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBy,
    DateTimeOffset? ClosedAt,
    string? ClosedBy,
    DateTimeOffset? CancelledAt,
    string? CancelledBy,
    decimal ReceivedQty,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    DateTimeOffset UpdatedAt);

/// <summary>One line. PoAmt = GrossAmt − DcAmt is the supply amount; TaxAmt is an estimate (BC-BR-547).</summary>
public sealed record PurchaseOrderLineDto(
    long Id,
    int Seq,
    long ItemId,
    string? ItemCd,
    string? ItemName,
    decimal PoQty,
    decimal PoPrc,
    decimal GrossAmt,
    decimal DcRate,
    decimal DcAmt,
    decimal PoAmt,
    long TaxCodeId,
    decimal SupplyAmt,
    decimal TaxAmt,
    DateOnly? DueDt,
    long? WarehouseId,
    decimal ReceivedQty);

/// <summary>
/// New or changed purchase order. Lines are the whole list in screen order: lines left out are deleted, lines without Id are added.
/// CurrencyCode defaults to the vendor's trade currency, then the base currency (MM-BR-031); ExchangeRate defaults to 1.
/// UpdatedAt is required on PUT.
/// </summary>
public sealed record SavePurchaseOrderRequest(
    long VendorId,
    DateOnly PoDt,
    string? CurrencyCode,
    decimal? ExchangeRate,
    string? Incoterms,
    DateOnly? DueDt,
    long? WarehouseId,
    string? PaymentTerms,
    string? Buyer,
    string? Remarks,
    IReadOnlyList<SavePurchaseOrderLineRequest> Lines,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// One line. DueDt and WarehouseId default to the header's, TaxCodeId to the vendor's default tax code (MM-BR-026, BC-BR-542).
/// DcRate is a percentage from 0 to 100 (MM-BR-025).
/// </summary>
public sealed record SavePurchaseOrderLineRequest(
    long? Id, long ItemId, string? ItemName, decimal PoQty, decimal PoPrc, decimal DcRate, DateOnly? DueDt, long? WarehouseId, long? TaxCodeId);

/// <summary>Approve, close or cancel. UpdatedAt must equal the stored value: no unsaved changes (MM-BR-020).</summary>
public sealed record PurchaseOrderActionRequest(DateTimeOffset UpdatedAt);

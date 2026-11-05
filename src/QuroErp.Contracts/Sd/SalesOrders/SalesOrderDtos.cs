namespace QuroErp.Contracts.Sd.SalesOrders;

/// <summary>Sales order API routes (SD-UC-002).</summary>
public static class SalesOrderRoutes
{
    /// <summary>GET list (query <see cref="SalesOrderSearchQuery"/>), POST new order; GET, PUT, DELETE {Base}/{id}.</summary>
    public const string Base = "api/sd/sales-orders";

    /// <summary>GET {Base}/line-defaults with <see cref="SalesOrderLineDefaultsQuery"/>: the values of a new line (SD-BR-016, 033, 401, 405).</summary>
    public const string LineDefaults = "line-defaults";

    /// <summary>POST {Base}/line-file with a multipart "file" and <see cref="SalesOrderLineFileQuery"/>: lines of a line or barcode file (SD-BR-017~022).</summary>
    public const string LineFile = "line-file";

    /// <summary>GET {Base}/discount-rates: the discount codes a line can take, with their rates (SD-BR-404).</summary>
    public const string DiscountRates = "discount-rates";

    /// <summary>Multipart field of the line file.</summary>
    public const string LineFileFormField = "file";

    /// <summary>GET {Base}/{id}/print/... PDFs (print-documents.md).</summary>
    public const string PrintSalesOrder = "print/sales-order.pdf";
    public const string PrintPickingList = "print/picking-list.pdf";
    public const string PrintSetComponentList = "print/set-component-list.pdf";

    /// <summary>Most rows the list returns, newest first.</summary>
    public const int MaxListSize = 200;
}

/// <summary>Line file kinds (SD-BR-017, SD-BR-022).</summary>
public static class SalesOrderLineFileKind
{
    /// <summary>"item code,qty" per line.</summary>
    public const string Item = "ITEM";

    /// <summary>"barcode,qty" per line; the barcode is bc_item.upc_code.</summary>
    public const string Barcode = "BARCODE";
}

/// <summary>List and find conditions; all optional. SoNo matches the start of the number.</summary>
public sealed record SalesOrderSearchQuery(string? SoNo, long? CustomerId, string? Status, DateOnly? FromDt, DateOnly? ToDt);

public sealed record SalesOrderSummaryDto(
    long Id, string SoNo, string Status, DateOnly SoDt, long CustomerId, string? CustomerName, string CurrencyCode, decimal TotSoAmt);

/// <summary>
/// One sales order. Amounts are computed by the server: SupplyAmt + TaxAmt = TotSoAmt (SD-BR-007, SD-BR-034), TotBaseAmt in base
/// currency; TaxAmt is an estimate (BC-BR-547). DcStatus is the customer's discount status, shown only. BillingFlowLocked is true
/// when a shipment or invoice exists, so the billing flow cannot change (SD-BR-035). Status is set by shipments (SD-BR-012).
/// CreatedAt, CreatedBy and UpdatedBy are the audit line of the screen; migrated rows may lack them.
/// </summary>
public sealed record SalesOrderDto(
    long Id,
    string SoNo,
    string Status,
    long CustomerId,
    string? CustomerName,
    DateOnly SoDt,
    DateOnly? ExpShipDt,
    DateOnly? DueDt,
    string CurrencyCode,
    decimal ExchangeRate,
    string BillingFlow,
    bool BillingFlowLocked,
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
    string? DcStatus,
    decimal SupplyAmt,
    decimal TaxAmt,
    decimal TotSoAmt,
    decimal TotBaseAmt,
    IReadOnlyList<SalesOrderLineDto> Lines,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CreatedAt = null,
    string? CreatedBy = null,
    string? UpdatedBy = null);

/// <summary>
/// One line. SoAmt = SoQty × SoPrc × (1 + DcRate / 100) (SD-BR-005); DcRate is a negative percentage from the discount code
/// (SD-BR-404). StdPrc is the item's standard sales price. ShipQty is shipped (SD-BR-012) and DnQty invoiced; InvNo is the
/// latest invoice of the line.
/// </summary>
public sealed record SalesOrderLineDto(
    long Id,
    int Seq,
    long ItemId,
    string? ItemCd,
    string? ItemName,
    decimal SoQty,
    decimal SoPrc,
    decimal SoAmt,
    string? DcCd,
    decimal DcRate,
    decimal? StdPrc,
    long TaxCodeId,
    decimal SupplyAmt,
    decimal TaxAmt,
    decimal ShipQty,
    decimal DnQty,
    string? InvNo,
    string? Unit = null);

/// <summary>
/// New or changed sales order. Lines are the whole list in screen order: lines left out are deleted, lines without Id are added.
/// Empty values default from the customer: Rep, Terms, FreightCharge, the ship-to (when every ship-to field is empty),
/// CurrencyCode (trade currency, then base currency, SD-BR-031), BillingFlow (SD-BR-035). ExchangeRate defaults to the latest
/// rate (BC-BR-412); ExpShipDt to SoDt; DueDt to the terms (SD-BR-008). UpdatedAt is required on PUT. The answers:
/// UpdateCustomerShipTo (SD-BR-009, 422 SD-W-009), UpdatePriceLevel (SD-BR-010, 422 SD-W-010) and ConfirmCreditLimit (SD-BR-309,
/// 422 SD-W-309). Null asks with 422 and confirmField; true goes on (and for the first two also updates the customer or price
/// level); false goes on without the update.
/// </summary>
public sealed record SaveSalesOrderRequest(
    long CustomerId,
    DateOnly SoDt,
    DateOnly? ExpShipDt,
    DateOnly? DueDt,
    string? CurrencyCode,
    decimal? ExchangeRate,
    string? BillingFlow,
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
    IReadOnlyList<SaveSalesOrderLineRequest> Lines,
    DateTimeOffset? UpdatedAt,
    bool? UpdateCustomerShipTo = null,
    bool? UpdatePriceLevel = null,
    bool? ConfirmCreditLimit = null);

/// <summary>
/// One line. SoQty defaults to 1 (SD-BR-016), SoPrc to the customer's price (SD-BR-401), TaxCodeId to the line tax default
/// (SD-BR-033). DcCd null on a new line takes the customer's discount status (SD-BR-405); an empty string means no discount.
/// </summary>
public sealed record SaveSalesOrderLineRequest(
    long? Id, long ItemId, string? ItemName, decimal? SoQty, decimal? SoPrc, string? DcCd, long? TaxCodeId);

/// <summary>The item by code, for the customer and ship-to state of the order. SoDt picks valid exemption certificates (default today).</summary>
public sealed record SalesOrderLineDefaultsQuery(long CustomerId, string ItemCd, string? ShipState, DateOnly? SoDt);

/// <summary>
/// Values of a new line (SD-BR-016, 033, 401, 405). Warning is BC-W-525 when the ship-to state has only expired exemption
/// certificates.
/// </summary>
public sealed record SalesOrderLineDefaultsDto(
    long ItemId,
    string ItemCd,
    string? ItemName,
    string? Unit,
    decimal SoQty,
    decimal SoPrc,
    decimal? StdPrc,
    string? DcCd,
    decimal DcRate,
    long? TaxCodeId,
    string? Warning);

/// <summary>A discount code and its rate, a negative percentage (SD-BR-404).</summary>
public sealed record SalesOrderDiscountRateDto(string DcCd, decimal DcRate);

/// <summary>Customer and ship-to of the order the file adds to, and the file kind (<see cref="SalesOrderLineFileKind"/>).</summary>
public sealed record SalesOrderLineFileQuery(long CustomerId, string Kind, string? ShipState, DateOnly? SoDt);

/// <summary>A file row that cannot be used: line number, the row as read and the reason (FORMAT, QTY, NOT_FOUND, NOT_IN_USE).</summary>
public sealed record SalesOrderLineFileErrorDto(int LineNo, string Value, string Reason);

/// <summary>
/// Lines to add to the grid, in file order with the file quantity (SD-BR-020), or the error rows and no lines (SD-BR-019,
/// the screen shows SD-E-019). Nothing is saved (SD-BR-021).
/// </summary>
public sealed record SalesOrderLineFileResult(IReadOnlyList<SalesOrderLineDefaultsDto> Lines, IReadOnlyList<SalesOrderLineFileErrorDto> Errors);

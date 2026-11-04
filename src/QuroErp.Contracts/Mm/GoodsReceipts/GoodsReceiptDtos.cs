namespace QuroErp.Contracts.Mm.GoodsReceipts;

/// <summary>Goods receipt API routes (MM-UC-004).</summary>
public static class GoodsReceiptRoutes
{
    /// <summary>GET list (query <see cref="GoodsReceiptSearchQuery"/>), POST new receipt; GET, PUT, DELETE {Base}/{id}.</summary>
    public const string Base = "api/mm/goods-receipts";

    /// <summary>GET {Base}/open-po-lines?vendorId: PO lines the receipt can import (MM-BR-113).</summary>
    public const string OpenPoLines = "open-po-lines";

    /// <summary>Most rows the list returns, newest first.</summary>
    public const int MaxListSize = 200;
}

/// <summary>List and find conditions; all optional. GrNo matches the start of the number.</summary>
public sealed record GoodsReceiptSearchQuery(string? GrNo, long? VendorId, DateOnly? FromDt, DateOnly? ToDt);

/// <param name="Status">OP, PA, CL or VO, computed from the line quantities (MM-BR-105).</param>
public sealed record GoodsReceiptSummaryDto(
    long Id, string GrNo, string Status, DateOnly GrDt, long VendorId, string? VendorName, string CurrencyCode, decimal TotGrAmt);

/// <summary>
/// One goods receipt. Amounts are computed by the server: SupplyAmt + TaxAmt = TotGrAmt (MM-BR-112, MM-BR-162), TotBaseAmt in
/// base currency (BC-BR-413). The receipt has no payable: the supplier invoice matches its lines (MM-BR-751).
/// </summary>
/// <param name="Status">OP, PA, CL or VO (MM-BR-105).</param>
/// <param name="WarehouseId">Default warehouse of new lines (MM-BR-703).</param>
public sealed record GoodsReceiptDto(
    long Id,
    string GrNo,
    string Status,
    long VendorId,
    string? VendorName,
    DateOnly GrDt,
    long WarehouseId,
    string? RefNo,
    string? Remarks,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal SupplyAmt,
    decimal TaxAmt,
    decimal TotGrAmt,
    decimal TotBaseAmt,
    IReadOnlyList<GoodsReceiptLineDto> Lines,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CreatedAt = null,
    string? CreatedBy = null,
    string? UpdatedBy = null);

/// <summary>
/// One receipt line. GrAmt = round(GrQty × GrPrc); SupplyAmt and TaxAmt are the estimate the supplier invoice starts from
/// (MM-BR-162). PoNo, PoSeq, PoQty and PoReceivedQty describe the PO line it came from, if any. InvoicedQty and ReturnedQty
/// above 0 lock the receipt (MM-BR-104).
/// </summary>
public sealed record GoodsReceiptLineDto(
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
    decimal GrQty,
    decimal GrPrc,
    decimal GrAmt,
    long TaxCodeId,
    string? TaxCode,
    decimal SupplyAmt,
    decimal TaxAmt,
    bool Deductible,
    long? PurchaseOrderLineId,
    string? PoNo,
    int? PoSeq,
    decimal? PoQty,
    decimal? PoReceivedQty,
    decimal InvoicedQty,
    decimal ReturnedQty);

/// <summary>
/// New or changed receipt. CurrencyCode and ExchangeRate come from the PO when lines come from one (MM-BR-161); without PO lines
/// they default to the vendor's currency and the receipt date's rate. WarehouseId defaults to the user's default warehouse, else
/// the company default (MM-BR-703).
/// </summary>
/// <param name="Lines">Every line to keep, unchanged and with its Id, and the new lines without an Id. A saved line left out is
/// deleted; a changed saved line is refused (MM-BR-107).</param>
/// <param name="UpdatedAt">Required on PUT: the value from the last GET (409 when it differs).</param>
/// <param name="ConfirmOverReceipt">The answer to MM-W-102 in WARN mode: true saves the over-receipt (MM-BR-102).</param>
public sealed record SaveGoodsReceiptRequest(
    long VendorId,
    DateOnly GrDt,
    long? WarehouseId,
    string? RefNo,
    string? Remarks,
    string? CurrencyCode,
    decimal? ExchangeRate,
    IReadOnlyList<SaveGoodsReceiptLineRequest> Lines,
    DateTimeOffset? UpdatedAt,
    bool? ConfirmOverReceipt = null);

/// <param name="WarehouseId">Null takes the header warehouse (MM-BR-703).</param>
/// <param name="LotId">A lot-managed item's lot; for a new line LotNo may name a new lot instead, empty makes one (MM-BR-353).</param>
/// <param name="TaxCodeId">Null takes the vendor's tax code (BC-BR-542).</param>
/// <param name="Deductible">KR input VAT is deducted (default); false adds it to cost (MM-BR-163, MM-BR-164).</param>
/// <param name="PurchaseOrderLineId">The PO line the line comes from; null for an item entered directly (MM-BR-114).</param>
public sealed record SaveGoodsReceiptLineRequest(
    long? Id,
    long ItemId,
    string? ItemName,
    long? WarehouseId,
    long? LotId,
    string? LotNo,
    decimal GrQty,
    decimal GrPrc,
    long? TaxCodeId,
    bool Deductible = true,
    long? PurchaseOrderLineId = null);

/// <summary>
/// A PO line the receipt can import: from an approved or partly received order of the vendor with RemainingQty above 0
/// (MM-BR-113, MM-BR-024). RemainingQty is the default quantity and WarehouseId the default warehouse.
/// </summary>
public sealed record OpenPoLineDto(
    long PurchaseOrderLineId,
    long PurchaseOrderId,
    string PoNo,
    int PoSeq,
    DateOnly PoDt,
    long ItemId,
    string? ItemCd,
    string? ItemName,
    string? Unit,
    long? WarehouseId,
    decimal PoQty,
    decimal ReceivedQty,
    decimal RemainingQty,
    decimal PoPrc,
    decimal DcRate,
    long TaxCodeId,
    string CurrencyCode,
    decimal ExchangeRate);

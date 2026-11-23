namespace QuroErp.Contracts.Mm.GoodsReceipts;

/// <summary>Goods receipt API routes (MM-UC-004).</summary>
public static class GoodsReceiptRoutes
{
    /// <summary>GET list (query <see cref="GoodsReceiptSearchQuery"/>), POST new receipt; GET, PUT, DELETE {Base}/{id}.</summary>
    public const string Base = "api/mm/goods-receipts";

    /// <summary>
    /// GET {Base}/open-po-lines?vendorId: PO lines the receipt can import (MM-BR-113). The PDA asks with poNo (and itemId for a
    /// scanned item) instead: the remaining lines of that one order (MM-BR-920, MM-BR-923).
    /// </summary>
    public const string OpenPoLines = "open-po-lines";

    /// <summary>GET {Base}/{id}/pallet-labels: the labels of the receipt's ACTIVE Pallets as a PDF (MM-BR-918, MM-BR-919).</summary>
    public const string PalletLabels = "pallet-labels";

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
/// <param name="LotId">The Lot the save made for a lot-managed item, else Lot '*' (MM-BR-353, MM-BR-363).</param>
/// <param name="LocationId">The Location the line received to; '*' by default (MM-BR-713).</param>
/// <param name="SupplierLotNo">Lot attributes of the line's Lot (MM-BR-364).</param>
/// <param name="PalletQty">Qty per Pallet; null made no Pallet (MM-BR-910).</param>
/// <param name="PalletNos">Numbers of the Pallets the line made, in number order.</param>
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
    decimal ReturnedQty,
    long? LocationId = null,
    string? LocationCode = null,
    string? SupplierLotNo = null,
    DateOnly? MfgDt = null,
    DateOnly? ExpiryDt = null,
    decimal? PalletQty = null,
    IReadOnlyList<string>? PalletNos = null);

/// <summary>
/// New or changed receipt. CurrencyCode and ExchangeRate come from the PO when lines come from one (MM-BR-161); without PO lines
/// they default to the vendor's currency and the receipt date's rate. WarehouseId defaults to the user's default warehouse, else
/// the company default (MM-BR-703).
/// </summary>
/// <param name="Lines">Every line to keep, unchanged and with its Id, and the new lines without an Id. A saved line left out is
/// deleted; a changed saved line is refused (MM-BR-107). Lot attributes of saved lines may change, also on an invoiced receipt
/// (MM-BR-364).</param>
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
/// <param name="TaxCodeId">Null takes the vendor's tax code (BC-BR-542).</param>
/// <param name="Deductible">KR input VAT is deducted (default); false adds it to cost (MM-BR-163, MM-BR-164).</param>
/// <param name="PurchaseOrderLineId">The PO line the line comes from; null for an item entered directly (MM-BR-114).</param>
/// <param name="LocationId">A Location of the line warehouse; null is '*'. Only a Location-managed item takes another (MM-BR-713).</param>
/// <param name="SupplierLotNo">Up to 30 characters. With MfgDt and ExpiryDt (not before MfgDt) only for a lot-managed item, whose
/// Lot the save numbers itself (MM-BR-353, MM-BR-364).</param>
/// <param name="PalletQty">Above 0 splits the qty into Pallets of this qty; null makes none. Stock items only (MM-BR-910).</param>
public sealed record SaveGoodsReceiptLineRequest(
    long? Id,
    long ItemId,
    string? ItemName,
    long? WarehouseId,
    decimal GrQty,
    decimal GrPrc,
    long? TaxCodeId,
    bool Deductible = true,
    long? PurchaseOrderLineId = null,
    long? LocationId = null,
    string? SupplierLotNo = null,
    DateOnly? MfgDt = null,
    DateOnly? ExpiryDt = null,
    decimal? PalletQty = null);

/// <summary>
/// A PO line the receipt can import: from an approved or partly received order of the vendor with RemainingQty above 0
/// (MM-BR-113, MM-BR-024). RemainingQty is the default quantity and WarehouseId the default warehouse. VendorId and VendorName
/// are the order's vendor, for the PDA that starts from a PO number; LotManaged and LocationManaged tell which line fields the
/// item takes (MM-BR-363, MM-BR-713).
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
    decimal ExchangeRate,
    long? VendorId = null,
    string? VendorName = null,
    bool LotManaged = false,
    bool LocationManaged = false);

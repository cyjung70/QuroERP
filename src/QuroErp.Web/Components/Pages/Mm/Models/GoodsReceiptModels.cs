using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// Goods receipt header on the goods receipt screen (MM-UC-004). <see cref="GoodsReceiptDto"/> is immutable, so the frame binds
/// to this. The warehouse is bound by its code; the page turns it into an id. The receipt has no payable, so there is no due
/// date, terms or balance (MM-BR-751).
/// </summary>
public sealed class GoodsReceiptHeader
{
    public long Id { get; set; }
    public string? GrNo { get; set; }
    public string? Status { get; set; }
    public long? VendorId { get; set; }
    public string? VendorName { get; set; }
    public DateOnly? GrDt { get; set; }
    public string? WarehouseCode { get; set; }
    public string? RefNo { get; set; }
    public string? Remarks { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal TotGrAmt { get; set; }

    /// <summary>The vendor's default tax code, copied to new lines (BC-BR-542). Not stored on the receipt.</summary>
    public string? VendorTaxCode { get; set; }

    /// <summary>The stored receipt has lines, so the receipt date cannot change (MM-BR-117).</summary>
    public bool HasSavedLines { get; set; }

    /// <summary>
    /// A line is matched by a supplier invoice or returned, so the receipt cannot change (MM-BR-104, MM-BR-790) except the Lot
    /// attributes of its lines (MM-BR-364).
    /// </summary>
    public bool IsMatched { get; set; }

    /// <summary>A line made Pallets, so the Pallet labels can be printed (MM-BR-918).</summary>
    public bool HasPallets { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public QuroAudit ToAudit() => new(CreatedBy, CreatedAt, UpdatedBy, UpdatedAt);

    /// <summary>A row of the find dialog: number, date, vendor, status and total.</summary>
    public static GoodsReceiptHeader From(GoodsReceiptSummaryDto receipt) => new()
    {
        Id = receipt.Id,
        GrNo = receipt.GrNo,
        Status = receipt.Status,
        VendorId = receipt.VendorId,
        VendorName = receipt.VendorName,
        GrDt = receipt.GrDt,
        CurrencyCode = receipt.CurrencyCode,
        TotGrAmt = receipt.TotGrAmt,
    };

    /// <param name="warehouseCodes">Warehouse code by id.</param>
    /// <param name="vendorTaxCode">The vendor's default tax code, read with the receipt.</param>
    public static GoodsReceiptHeader From(GoodsReceiptDto receipt, IReadOnlyDictionary<long, string> warehouseCodes, string? vendorTaxCode) => new()
    {
        Id = receipt.Id,
        GrNo = receipt.GrNo,
        Status = receipt.Status,
        VendorId = receipt.VendorId,
        VendorName = receipt.VendorName,
        GrDt = receipt.GrDt,
        WarehouseCode = warehouseCodes.GetValueOrDefault(receipt.WarehouseId),
        RefNo = receipt.RefNo,
        Remarks = receipt.Remarks,
        CurrencyCode = receipt.CurrencyCode,
        ExchangeRate = receipt.ExchangeRate,
        TotGrAmt = receipt.TotGrAmt,
        VendorTaxCode = vendorTaxCode,
        HasSavedLines = receipt.Lines.Count > 0,
        IsMatched = receipt.Lines.Any(l => l.InvoicedQty > 0 || l.ReturnedQty > 0),
        HasPallets = receipt.Lines.Any(l => l.PalletNos is { Count: > 0 }),
        UpdatedAt = receipt.UpdatedAt,
        CreatedAt = receipt.CreatedAt,
        CreatedBy = receipt.CreatedBy,
        UpdatedBy = receipt.UpdatedBy,
    };

    /// <summary>The save request with the kept lines in screen order (saved lines left out are deleted, MM-BR-107).</summary>
    /// <param name="confirmOverReceipt">The answer to MM-W-102, or null to let the API ask.</param>
    public SaveGoodsReceiptRequest ToRequest(
        bool isNew, long? warehouseId, IEnumerable<GoodsReceiptLine> lines, bool? confirmOverReceipt) =>
        new(
            VendorId ?? 0,
            GrDt ?? DateOnly.FromDateTime(DateTime.Today),
            warehouseId,
            Clean(RefNo),
            Clean(Remarks),
            CurrencyCode,
            ExchangeRate,
            [.. lines.Select(l => l.ToRequest())],
            isNew ? null : UpdatedAt,
            confirmOverReceipt);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A line of the goods receipt grid (mm.goods-receipt.entry). Codes are shown; the ids are hidden columns. The Lot is made by the
/// save (MM-BR-353); the user gives its attributes (MM-BR-364), the Location (MM-BR-713) and the Pallet qty (MM-BR-910).
/// </summary>
public sealed class GoodsReceiptLine
{
    /// <summary>The system Location and Lot (MM-BR-713, MM-BR-363).</summary>
    public const string System = "*";

    /// <summary>The Lot attribute cells, which a saved line of a lot-managed item can still change (MM-BR-364).</summary>
    public static IReadOnlyList<string> LotAttributeFields { get; } =
        [nameof(SupplierLotNo), nameof(MfgDt), nameof(ExpiryDt)];

    public long Id { get; set; }
    public int? Seq { get; set; }
    public long? ItemId { get; set; }
    public string? ItemCd { get; set; }
    public string? ItemName { get; set; }
    public string? Unit { get; set; }
    public long? WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }

    /// <summary>The item keeps stock by lot, so the save makes the line a new Lot with these attributes (MM-BR-353, MM-BR-364).</summary>
    public bool LotManaged { get; set; }

    /// <summary>The item may keep stock on Locations other than '*' (MM-BR-713).</summary>
    public bool LocationManaged { get; set; }

    /// <summary>An Inventory item: it has stock, so it can have Pallets (MM-BR-910). A Service item has none.</summary>
    public bool StockItem { get; set; }

    public long? LocationId { get; set; }
    public string? LocationCode { get; set; } = System;
    public long? LotId { get; set; }
    public string? LotNo { get; set; }
    public string? SupplierLotNo { get; set; }
    public DateOnly? MfgDt { get; set; }
    public DateOnly? ExpiryDt { get; set; }
    public decimal? PalletQty { get; set; }

    /// <summary>The Pallets the saved line made, in number order.</summary>
    public string? PalletNos { get; set; }
    public decimal? GrQty { get; set; }
    public decimal? GrPrc { get; set; }
    public decimal? GrAmt { get; set; }
    public long? TaxCodeId { get; set; }
    public string? TaxCode { get; set; }
    public decimal? TaxAmt { get; set; }
    public bool Deductible { get; set; } = true;
    public long? PurchaseOrderLineId { get; set; }
    public string? PoNo { get; set; }
    public int? PoSeq { get; set; }
    public decimal? PoQty { get; set; }
    public decimal? PoReceivedQty { get; set; }
    public decimal InvoicedQty { get; set; }
    public decimal ReturnedQty { get; set; }

    /// <summary>A saved line cannot be changed, only deleted and entered again (MM-BR-107).</summary>
    public bool IsSaved() => Id != 0;

    public static GoodsReceiptLine From(GoodsReceiptLineDto line, IReadOnlyDictionary<long, string> taxCodes) => new()
    {
        Id = line.Id,
        Seq = line.Seq,
        ItemId = line.ItemId,
        ItemCd = line.ItemCd,
        ItemName = line.ItemName,
        Unit = line.Unit,
        WarehouseId = line.WarehouseId,
        WarehouseCode = line.WarehouseCode,
        LotManaged = line.LotNo is { } lotNo && lotNo != System,
        LocationId = line.LocationId,
        LocationCode = line.LocationCode ?? System,
        LotId = line.LotId,
        LotNo = line.LotNo,
        SupplierLotNo = line.SupplierLotNo,
        MfgDt = line.MfgDt,
        ExpiryDt = line.ExpiryDt,
        PalletQty = line.PalletQty,
        PalletNos = line.PalletNos is { Count: > 0 } pallets ? string.Join(", ", pallets) : null,
        GrQty = line.GrQty,
        GrPrc = line.GrPrc,
        GrAmt = line.GrAmt,
        TaxCodeId = line.TaxCodeId,
        TaxCode = line.TaxCode ?? taxCodes.GetValueOrDefault(line.TaxCodeId),
        TaxAmt = line.TaxAmt,
        Deductible = line.Deductible,
        PurchaseOrderLineId = line.PurchaseOrderLineId,
        PoNo = line.PoNo,
        PoSeq = line.PoSeq,
        PoQty = line.PoQty,
        PoReceivedQty = line.PoReceivedQty,
        InvoicedQty = line.InvoicedQty,
        ReturnedQty = line.ReturnedQty,
    };

    /// <summary>
    /// A line imported from an open PO line: quantity = the remaining quantity, warehouse = the PO line's or else the header's,
    /// price = the PO price after its discount (MM-BR-113).
    /// </summary>
    public static GoodsReceiptLine From(
        OpenPoLineDto line, string? warehouseCode, long? warehouseId, string? taxCode, decimal netPrice) => new()
        {
            ItemId = line.ItemId,
            ItemCd = line.ItemCd,
            ItemName = line.ItemName,
            Unit = line.Unit,
            WarehouseId = warehouseId,
            WarehouseCode = warehouseCode,
            GrQty = line.RemainingQty,
            GrPrc = netPrice,
            TaxCodeId = line.TaxCodeId,
            TaxCode = taxCode,
            PurchaseOrderLineId = line.PurchaseOrderLineId,
            PoNo = line.PoNo,
            PoSeq = line.PoSeq,
            PoQty = line.PoQty,
            PoReceivedQty = line.ReceivedQty,
        };

    /// <summary>
    /// A saved line goes back as it was stored, with its Id and its Lot attributes, which may change (MM-BR-364). A new line
    /// sends its Location (null is '*'), its Pallet qty and, for a lot-managed item, the attributes of the Lot the save makes.
    /// </summary>
    public SaveGoodsReceiptLineRequest ToRequest() =>
        new(
            IsSaved() ? Id : null,
            ItemId ?? 0,
            ItemName,
            WarehouseId,
            GrQty ?? 0,
            GrPrc ?? 0,
            TaxCodeId,
            Deductible,
            PurchaseOrderLineId,
            LocationId,
            LotManaged && !string.IsNullOrWhiteSpace(SupplierLotNo) ? SupplierLotNo.Trim() : null,
            LotManaged ? MfgDt : null,
            LotManaged ? ExpiryDt : null,
            PalletQty);
}

/// <summary>Goods receipt statuses: how much of the receipt the supplier invoices have matched (MM-BR-105).</summary>
public static class GoodsReceiptStatus
{
    public const string Open = "OP";
    public const string PartlyMatched = "PA";
    public const string Matched = "CL";
    public const string Void = "VO";

    public static IReadOnlyList<string> All { get; } = [Open, PartlyMatched, Matched, Void];

    public static ChipKind ChipOf(string? status) => status switch
    {
        Open => ChipKind.Brand,
        PartlyMatched => ChipKind.Warn,
        Matched => ChipKind.Ok,
        Void => ChipKind.Danger,
        _ => ChipKind.Neutral,
    };

    /// <summary>Save, line changes and delete: nothing matched or returned yet (MM-BR-104, MM-BR-790).</summary>
    public static bool CanSave(GoodsReceiptHeader receipt) => !receipt.IsMatched && receipt.Status is null or Open;

    /// <summary>A matched or returned receipt still saves the Lot attributes of its lines (MM-BR-364).</summary>
    public static bool CanSaveLotAttributes(GoodsReceiptHeader receipt) => CanSave(receipt) || receipt.IsMatched;

    public static bool CanDelete(GoodsReceiptHeader receipt) => CanSave(receipt);
}

/// <summary>Number formats of the receipt line columns in the document decimals (BC-BR-421~423).</summary>
public static class GoodsReceiptFormats
{
    public static IReadOnlyDictionary<string, string> Of(PurchaseOrderDecimals decimals) => new Dictionary<string, string>
    {
        ["grQty"] = N(decimals.Qty),
        ["poQty"] = N(decimals.Qty),
        ["poReceivedQty"] = N(decimals.Qty),
        ["palletQty"] = N(decimals.Qty),
        ["grPrc"] = N(decimals.Price),
        ["grAmt"] = N(decimals.Amount),
        ["taxAmt"] = N(decimals.Amount),
    };

    private static string N(int decimals) => "N" + decimals.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

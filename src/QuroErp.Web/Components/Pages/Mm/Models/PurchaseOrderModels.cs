using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// Purchase order header on the purchase order screen (MM-UC-003). <see cref="PurchaseOrderDto"/> is immutable, so the frame
/// binds to this. The warehouse and the default tax code are bound by their codes; the page turns them into ids. The default
/// tax code is not stored: it is the vendor's, copied to new lines (BC-BR-542).
/// </summary>
public sealed class PurchaseOrderHeader
{
    public long Id { get; set; }
    public string? PoNo { get; set; }
    public string? Status { get; set; }
    public long? VendorId { get; set; }
    public string? VendorName { get; set; }
    public DateOnly? PoDt { get; set; }
    public DateOnly? DueDt { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? ExchangeRate { get; set; }
    public string? WarehouseCode { get; set; }
    public string? TaxCode { get; set; }
    public string? Incoterms { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Buyer { get; set; }
    public string? Remarks { get; set; }
    public decimal TotPoAmt { get; set; }
    public int RevisionNo { get; set; }
    public decimal ReceivedQty { get; set; }

    /// <summary>The stored order has lines, so the vendor cannot change (MM-BR-031).</summary>
    public bool HasSavedLines { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }

    public QuroAudit ToAudit() => new(CreatedBy, CreatedAt, UpdatedBy, UpdatedAt, ApprovedBy, ApprovedAt);

    /// <summary>A row of the find dialog: number, date, vendor, status and total.</summary>
    public static PurchaseOrderHeader From(PurchaseOrderSummaryDto order) => new()
    {
        Id = order.Id,
        PoNo = order.PoNo,
        Status = order.Status,
        VendorId = order.VendorId,
        VendorName = order.VendorName,
        PoDt = order.PoDt,
        CurrencyCode = order.CurrencyCode,
        TotPoAmt = order.TotPoAmt,
    };

    /// <param name="warehouseCodes">Warehouse code by id.</param>
    /// <param name="taxCode">The vendor's default tax code, read with the order.</param>
    public static PurchaseOrderHeader From(PurchaseOrderDto order, IReadOnlyDictionary<long, string> warehouseCodes, string? taxCode) => new()
    {
        Id = order.Id,
        PoNo = order.PoNo,
        Status = order.Status,
        VendorId = order.VendorId,
        VendorName = order.VendorName,
        PoDt = order.PoDt,
        DueDt = order.DueDt,
        CurrencyCode = order.CurrencyCode,
        ExchangeRate = order.ExchangeRate,
        WarehouseCode = order.WarehouseId is { } id ? warehouseCodes.GetValueOrDefault(id) : null,
        TaxCode = taxCode,
        Incoterms = order.Incoterms,
        PaymentTerms = order.PaymentTerms,
        Buyer = order.Buyer,
        Remarks = order.Remarks,
        TotPoAmt = order.TotPoAmt,
        RevisionNo = order.RevisionNo,
        ReceivedQty = order.ReceivedQty,
        HasSavedLines = order.Lines.Count > 0,
        UpdatedAt = order.UpdatedAt,
        CreatedAt = order.CreatedAt,
        CreatedBy = order.CreatedBy,
        UpdatedBy = order.UpdatedBy,
        ApprovedAt = order.ApprovedAt,
        ApprovedBy = order.ApprovedBy,
    };

    /// <summary>
    /// The save request with the kept lines in screen order (lines left out are deleted, MM-UC-003).
    /// </summary>
    /// <param name="updateVendorPrice">The answer to MM-W-016, or null to let the API ask.</param>
    public SavePurchaseOrderRequest ToRequest(
        bool isNew, long? warehouseId, IEnumerable<PurchaseOrderLine> lines, bool? updateVendorPrice) =>
        new(
            VendorId ?? 0,
            PoDt ?? DateOnly.FromDateTime(DateTime.Today),
            CurrencyCode,
            ExchangeRate,
            Clean(Incoterms),
            DueDt,
            warehouseId,
            Clean(PaymentTerms),
            Clean(Buyer),
            Clean(Remarks),
            [.. lines.Select(l => l.ToRequest())],
            isNew ? null : UpdatedAt,
            updateVendorPrice);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A line of the purchase order grid (mm.purchase-order.entry). Codes are shown; the ids are hidden columns.</summary>
public sealed class PurchaseOrderLine
{
    public long Id { get; set; }
    public int? Seq { get; set; }
    public long? ItemId { get; set; }
    public string? ItemCd { get; set; }
    public string? ItemName { get; set; }
    public string? Unit { get; set; }
    public long? WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public decimal? PoQty { get; set; }
    public decimal? PoPrc { get; set; }
    public decimal? DcRate { get; set; }
    public decimal? GrossAmt { get; set; }
    public decimal? DcAmt { get; set; }
    public decimal? PoAmt { get; set; }
    public long? TaxCodeId { get; set; }
    public string? TaxCode { get; set; }
    public decimal? TaxAmt { get; set; }
    public DateOnly? DueDt { get; set; }
    public decimal ReceivedQty { get; set; }

    /// <summary>A received line is locked in every cell and cannot be removed (MM-BR-013).</summary>
    public bool IsReceived() => ReceivedQty > 0;

    public static PurchaseOrderLine From(
        PurchaseOrderLineDto line, IReadOnlyDictionary<long, string> warehouseCodes, IReadOnlyDictionary<long, string> taxCodes) => new()
        {
            Id = line.Id,
            Seq = line.Seq,
            ItemId = line.ItemId,
            ItemCd = line.ItemCd,
            ItemName = line.ItemName,
            Unit = line.Unit,
            WarehouseId = line.WarehouseId,
            WarehouseCode = line.WarehouseId is { } id ? warehouseCodes.GetValueOrDefault(id) : null,
            PoQty = line.PoQty,
            PoPrc = line.PoPrc,
            DcRate = line.DcRate,
            GrossAmt = line.GrossAmt,
            DcAmt = line.DcAmt,
            PoAmt = line.PoAmt,
            TaxCodeId = line.TaxCodeId,
            TaxCode = taxCodes.GetValueOrDefault(line.TaxCodeId),
            TaxAmt = line.TaxAmt,
            DueDt = line.DueDt,
            ReceivedQty = line.ReceivedQty,
        };

    public SavePurchaseOrderLineRequest ToRequest() =>
        new(Id == 0 ? null : Id, ItemId ?? 0, ItemName, PoQty ?? 0, PoPrc ?? 0, DcRate ?? 0, DueDt, WarehouseId, TaxCodeId);
}

/// <summary>Purchase order statuses (MM-BR-019), their chips and the commands each allows (purchase-order.md "상태와 버튼").</summary>
public static class PurchaseOrderStatus
{
    public const string Draft = "DR";
    public const string Approved = "PO";
    public const string PartlyReceived = "RE";
    public const string Received = "CL";
    public const string Closed = "MC";
    public const string Cancelled = "CN";

    public static ChipKind ChipOf(string? status) => status switch
    {
        Approved => ChipKind.Brand,
        PartlyReceived => ChipKind.Warn,
        Received => ChipKind.Ok,
        Cancelled => ChipKind.Danger,
        _ => ChipKind.Neutral,
    };

    /// <summary>Save and line edit: DR, or PO before any receipt (MM-BR-013, MM-BR-021).</summary>
    public static bool CanSave(PurchaseOrderHeader order) => order.ReceivedQty == 0 && order.Status is Draft or Approved;

    public static bool CanDelete(PurchaseOrderHeader order) => CanSave(order);

    public static bool CanApprove(PurchaseOrderHeader order) => order.Status == Draft;

    public static bool CanClose(PurchaseOrderHeader order) => order.Status is Approved or PartlyReceived;

    public static bool CanCancel(PurchaseOrderHeader order) => order.Status == Approved && order.ReceivedQty == 0;
}

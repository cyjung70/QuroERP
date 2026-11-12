using QuroErp.Contracts.Mm.SupplierInvoices;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// Supplier invoice header on the supplier invoice screen (MM-UC-032). <see cref="SupplierInvoiceDto"/> is immutable, so the frame
/// binds to this. The currency is the matched receipts' (MM-BR-756); the amounts are the server's (MM-BR-755).
/// </summary>
public sealed class SupplierInvoiceHeader
{
    public long Id { get; set; }
    public string? InvoiceNo { get; set; }

    /// <summary>INV, or DM for a Debit Memo (MM-BR-762).</summary>
    public string? Type { get; set; }

    /// <summary>OP, PA, CL or VO (MM-BR-761).</summary>
    public string? Status { get; set; }

    public long? VendorId { get; set; }
    public string? VendorName { get; set; }

    /// <summary>The vendor's invoice number, one per vendor (MM-BR-764).</summary>
    public string? RefNo { get; set; }

    public DateOnly? InvoiceDt { get; set; }
    public DateOnly? DueDt { get; set; }
    public string? Terms { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? ExchangeRate { get; set; }
    public bool TaxInvoiceReceived { get; set; }
    public string? Remarks { get; set; }
    public decimal TotAmt { get; set; }

    /// <summary>Amount paid (MM-BR-951); above 0 locks the invoice (MM-BR-760).</summary>
    public decimal ClsAmt { get; set; }

    public decimal? BalAmt { get; set; }

    /// <summary>The stored invoice has lines, so its date, currency and rate cannot change (MM-BR-760).</summary>
    public bool HasSavedLines { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public QuroAudit ToAudit() => new(CreatedBy, CreatedAt, UpdatedBy, UpdatedAt);

    /// <summary>A row of the find dialog: number, date, vendor, vendor invoice number, status and total.</summary>
    public static SupplierInvoiceHeader From(SupplierInvoiceSummaryDto invoice) => new()
    {
        Id = invoice.Id,
        InvoiceNo = invoice.InvoiceNo,
        Type = invoice.Type,
        Status = invoice.Status,
        VendorId = invoice.VendorId,
        VendorName = invoice.VendorName,
        RefNo = invoice.RefNo,
        InvoiceDt = invoice.InvoiceDt,
        DueDt = invoice.DueDt,
        CurrencyCode = invoice.CurrencyCode,
        TotAmt = invoice.TotAmt,
        BalAmt = invoice.BalAmt,
    };

    public static SupplierInvoiceHeader From(SupplierInvoiceDto invoice) => new()
    {
        Id = invoice.Id,
        InvoiceNo = invoice.InvoiceNo,
        Type = invoice.Type,
        Status = invoice.Status,
        VendorId = invoice.VendorId,
        VendorName = invoice.VendorName,
        RefNo = invoice.RefNo,
        InvoiceDt = invoice.InvoiceDt,
        DueDt = invoice.DueDt,
        Terms = invoice.Terms,
        CurrencyCode = invoice.CurrencyCode,
        ExchangeRate = invoice.ExchangeRate,
        TaxInvoiceReceived = invoice.TaxInvoiceReceived,
        Remarks = invoice.Remarks,
        TotAmt = invoice.TotAmt,
        ClsAmt = invoice.ClsAmt,
        BalAmt = invoice.BalAmt,
        HasSavedLines = invoice.Lines.Count > 0,
        UpdatedAt = invoice.UpdatedAt,
        CreatedAt = invoice.CreatedAt,
        CreatedBy = invoice.CreatedBy,
        UpdatedBy = invoice.UpdatedBy,
    };

    /// <summary>The save request with the kept lines in screen order (saved lines left out are deleted, MM-BR-760).</summary>
    /// <param name="confirmPriceDifference">The answer to MM-W-754, or null to let the API ask.</param>
    public SaveSupplierInvoiceRequest ToRequest(bool isNew, IEnumerable<SupplierInvoiceLine> lines, bool? confirmPriceDifference) =>
        new(
            VendorId ?? 0,
            Clean(RefNo),
            InvoiceDt ?? DateOnly.FromDateTime(DateTime.Today),
            DueDt,
            Clean(Terms),
            ExchangeRate,
            TaxInvoiceReceived,
            Clean(Remarks),
            [.. lines.Select(l => l.ToRequest())],
            isNew ? null : UpdatedAt,
            confirmPriceDifference);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A line of the supplier invoice grid (mm.supplier-invoice.entry) and the receipt line it matches.</summary>
public sealed class SupplierInvoiceLine
{
    public long Id { get; set; }
    public int? Seq { get; set; }
    public long? GoodsReceiptLineId { get; set; }
    public string? GrNo { get; set; }
    public int? GrSeq { get; set; }
    public long? ItemId { get; set; }
    public string? ItemCd { get; set; }
    public string? ItemName { get; set; }
    public string? Unit { get; set; }
    public decimal? GrQty { get; set; }
    public decimal? GrPrc { get; set; }
    public decimal? Qty { get; set; }
    public decimal? Prc { get; set; }
    public decimal? SupplyAmt { get; set; }
    public long? TaxCodeId { get; set; }
    public string? TaxCode { get; set; }
    public decimal? TaxAmt { get; set; }
    public bool Deductible { get; set; } = true;

    /// <summary>The price variance posted to stock in base currency (MM-BR-758); the server's, empty on a new line.</summary>
    public decimal? PvAmt { get; set; }

    /// <summary>A saved line cannot be changed, only deleted and entered again (MM-BR-760).</summary>
    public bool IsSaved() => Id != 0;

    public static SupplierInvoiceLine From(SupplierInvoiceLineDto line) => new()
    {
        Id = line.Id,
        Seq = line.Seq,
        GoodsReceiptLineId = line.GoodsReceiptLineId,
        GrNo = line.GrNo,
        GrSeq = line.GrSeq,
        ItemId = line.ItemId,
        ItemCd = line.ItemCd,
        ItemName = line.ItemName,
        Unit = line.Unit,
        GrQty = line.GrQty,
        GrPrc = line.GrPrc,
        Qty = line.Qty,
        Prc = line.Prc,
        SupplyAmt = line.SupplyAmt,
        TaxCodeId = line.TaxCodeId,
        TaxCode = line.TaxCode,
        TaxAmt = line.TaxAmt,
        Deductible = line.Deductible,
        PvAmt = line.PvAmt,
    };

    /// <summary>
    /// A line imported from an open receipt line: quantity = the quantity not yet invoiced, price, tax code and deduction = the
    /// receipt line's (MM-BR-753, MM-BR-754, MM-BR-755, MM-BR-763).
    /// </summary>
    public static SupplierInvoiceLine From(OpenReceiptLineDto line) => new()
    {
        GoodsReceiptLineId = line.GoodsReceiptLineId,
        GrNo = line.GrNo,
        GrSeq = line.GrSeq,
        ItemId = line.ItemId,
        ItemCd = line.ItemCd,
        ItemName = line.ItemName,
        Unit = line.Unit,
        GrQty = line.GrQty,
        GrPrc = line.GrPrc,
        Qty = line.OpenQty,
        Prc = line.GrPrc,
        TaxCodeId = line.TaxCodeId,
        TaxCode = line.TaxCode,
        Deductible = line.Deductible,
    };

    /// <summary>A saved line goes back as it was stored, with its Id; a new line sends what the user entered.</summary>
    public SaveSupplierInvoiceLineRequest ToRequest() =>
        new(IsSaved() ? Id : null, GoodsReceiptLineId ?? 0, Qty ?? 0, Prc, TaxCodeId, Deductible);
}

/// <summary>Supplier invoice types: an invoice, or a Debit Memo its vendor return made (MM-BR-762).</summary>
public static class SupplierInvoiceType
{
    public const string Invoice = "INV";
    public const string DebitMemo = "DM";
}

/// <summary>Supplier invoice statuses: how much of the invoice is paid (MM-BR-761).</summary>
public static class SupplierInvoiceStatus
{
    public const string Open = "OP";
    public const string PartlyPaid = "PA";
    public const string Paid = "CL";
    public const string Void = "VO";

    public static IReadOnlyList<string> All { get; } = [Open, PartlyPaid, Paid, Void];

    public static ChipKind ChipOf(string? status) => status switch
    {
        Open => ChipKind.Brand,
        PartlyPaid => ChipKind.Warn,
        Paid => ChipKind.Ok,
        Void => ChipKind.Danger,
        _ => ChipKind.Neutral,
    };

    /// <summary>Save, line changes and delete: an open invoice with nothing paid that is not a Debit Memo (MM-BR-760, MM-BR-762).</summary>
    public static bool CanSave(SupplierInvoiceHeader invoice) =>
        invoice.Type != SupplierInvoiceType.DebitMemo && invoice.ClsAmt == 0m && invoice.Status is null or Open;

    public static bool CanDelete(SupplierInvoiceHeader invoice) => CanSave(invoice);
}

/// <summary>Number formats of the invoice line columns: document decimals, the PV amount in base currency (BC-BR-421~423).</summary>
public static class SupplierInvoiceFormats
{
    public static IReadOnlyDictionary<string, string> Of(PurchaseOrderDecimals decimals) => new Dictionary<string, string>
    {
        ["grQty"] = N(decimals.Qty),
        ["qty"] = N(decimals.Qty),
        ["grPrc"] = N(decimals.Price),
        ["prc"] = N(decimals.Price),
        ["supplyAmt"] = N(decimals.Amount),
        ["taxAmt"] = N(decimals.Amount),
        ["pvAmt"] = N(decimals.BaseAmount),
    };

    private static string N(int decimals) => "N" + decimals.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

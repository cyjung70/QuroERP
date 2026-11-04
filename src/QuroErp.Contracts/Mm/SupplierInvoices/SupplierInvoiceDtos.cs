namespace QuroErp.Contracts.Mm.SupplierInvoices;

/// <summary>Supplier invoice API routes (MM-UC-032).</summary>
public static class SupplierInvoiceRoutes
{
    /// <summary>GET list (query <see cref="SupplierInvoiceSearchQuery"/>), POST new invoice; GET, PUT, DELETE {Base}/{id}.</summary>
    public const string Base = "api/mm/supplier-invoices";

    /// <summary>GET {Base}/open-receipt-lines?vendorId&amp;currencyCode&amp;excludeInvoiceId: receipt lines to import (MM-BR-763).</summary>
    public const string OpenReceiptLines = "open-receipt-lines";

    /// <summary>Most rows the list returns, newest first.</summary>
    public const int MaxListSize = 200;
}

/// <summary>List and find conditions; all optional. InvoiceNo matches the start of the number.</summary>
/// <param name="Type">INV or DM.</param>
/// <param name="Status">OP, PA, CL or VO (MM-BR-761).</param>
public sealed record SupplierInvoiceSearchQuery(
    string? InvoiceNo, long? VendorId, DateOnly? FromDt, DateOnly? ToDt, string? Type = null, string? Status = null);

/// <param name="Type">INV, or DM for a Debit Memo (MM-BR-762).</param>
/// <param name="Status">OP, PA, CL or VO (MM-BR-761).</param>
public sealed record SupplierInvoiceSummaryDto(
    long Id, string InvoiceNo, string Type, string Status, DateOnly InvoiceDt, DateOnly DueDt, long VendorId, string? VendorName,
    string RefNo, string CurrencyCode, decimal TotAmt, decimal BalAmt);

/// <summary>
/// One supplier invoice. Amounts are computed by the server: SupplyAmt + TaxAmt = TotAmt, BalAmt = TotAmt − ClsAmt (MM-BR-755),
/// TotBaseAmt in base currency (BC-BR-413).
/// </summary>
/// <param name="Type">INV, or DM for a Debit Memo, which only its vendor return changes (MM-BR-762).</param>
/// <param name="Status">OP, PA, CL or VO (MM-BR-761).</param>
/// <param name="RefNo">The vendor's invoice number, one per vendor (MM-BR-764).</param>
/// <param name="ClsAmt">Amount paid (MM-BR-951); above 0 locks the invoice (MM-BR-760).</param>
/// <param name="VendorReturnId">The vendor return of a Debit Memo.</param>
public sealed record SupplierInvoiceDto(
    long Id,
    string InvoiceNo,
    string Type,
    string Status,
    long VendorId,
    string? VendorName,
    string RefNo,
    DateOnly InvoiceDt,
    DateOnly DueDt,
    string? Terms,
    string CurrencyCode,
    decimal ExchangeRate,
    bool TaxInvoiceReceived,
    string? Remarks,
    decimal SupplyAmt,
    decimal TaxAmt,
    decimal TotAmt,
    decimal TotBaseAmt,
    decimal ClsAmt,
    decimal BalAmt,
    long? VendorReturnId,
    IReadOnlyList<SupplierInvoiceLineDto> Lines,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CreatedAt = null,
    string? CreatedBy = null,
    string? UpdatedBy = null);

/// <summary>
/// One invoice line and the receipt line it matches (GrNo, GrSeq, GrQty, GrPrc). PvAmt is the price variance posted to stock in
/// base currency (MM-BR-758).
/// </summary>
public sealed record SupplierInvoiceLineDto(
    long Id,
    int Seq,
    long? GoodsReceiptLineId,
    string? GrNo,
    int? GrSeq,
    long ItemId,
    string? ItemCd,
    string? ItemName,
    string? Unit,
    decimal? GrQty,
    decimal? GrPrc,
    decimal Qty,
    decimal Prc,
    long TaxCodeId,
    string? TaxCode,
    decimal SupplyAmt,
    decimal TaxAmt,
    bool Deductible,
    decimal PvAmt);

/// <summary>
/// New or changed invoice. The currency is the matched receipts' (MM-BR-756); ExchangeRate defaults to the invoice date's rate,
/// DueDt to the invoice date and Terms to the vendor's. While the invoice has saved lines, InvoiceDt, the currency and
/// ExchangeRate cannot change (422 MM-E-760).
/// </summary>
/// <param name="Lines">Every line to keep, unchanged and with its Id, and the new lines without an Id. A saved line left out is
/// deleted; a changed saved line is refused (MM-BR-760).</param>
/// <param name="UpdatedAt">Required on PUT: the value from the last GET (409 when it differs).</param>
/// <param name="ConfirmPriceDifference">The answer to MM-W-754: true saves prices that differ from the PO (MM-BR-754).</param>
public sealed record SaveSupplierInvoiceRequest(
    long VendorId,
    string? RefNo,
    DateOnly InvoiceDt,
    DateOnly? DueDt,
    string? Terms,
    decimal? ExchangeRate,
    bool TaxInvoiceReceived,
    string? Remarks,
    IReadOnlyList<SaveSupplierInvoiceLineRequest> Lines,
    DateTimeOffset? UpdatedAt,
    bool? ConfirmPriceDifference = null);

/// <param name="Prc">Null takes the receipt line's price (MM-BR-754).</param>
/// <param name="TaxCodeId">Null takes the receipt line's tax code (MM-BR-755).</param>
/// <param name="Deductible">Null takes the receipt line's value: KR input VAT is deducted, false adds it to cost (MM-BR-164).</param>
public sealed record SaveSupplierInvoiceLineRequest(
    long? Id,
    long GoodsReceiptLineId,
    decimal Qty,
    decimal? Prc = null,
    long? TaxCodeId = null,
    bool? Deductible = null);

/// <summary>
/// A goods receipt line the invoice can match (MM-BR-763): of the vendor, in the currency, with OpenQty above 0. OpenQty is the
/// default quantity; GrPrc, TaxCodeId and Deductible are the defaults of the line (MM-BR-754, MM-BR-755). PoPrc is the PO line's
/// net price the invoice price is compared with, null without a PO.
/// </summary>
public sealed record OpenReceiptLineDto(
    long GoodsReceiptLineId,
    long GoodsReceiptId,
    string GrNo,
    int GrSeq,
    DateOnly GrDt,
    long ItemId,
    string? ItemCd,
    string? ItemName,
    string? Unit,
    decimal GrQty,
    decimal ReturnedQty,
    decimal InvoicedQty,
    decimal OpenQty,
    decimal GrPrc,
    decimal? PoPrc,
    long TaxCodeId,
    string? TaxCode,
    bool Deductible,
    string CurrencyCode,
    decimal ExchangeRate);

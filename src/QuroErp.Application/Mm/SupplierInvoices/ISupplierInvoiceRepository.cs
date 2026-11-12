using System.Data;
using QuroErp.Contracts.Mm.SupplierInvoices;
using QuroErp.Domain.Mm.SupplierInvoices;

namespace QuroErp.Application.Mm.SupplierInvoices;

/// <summary>mm_supplier_invoice row with the vendor name.</summary>
public sealed record SupplierInvoiceRow(
    long Id, string InvoiceNo, string Type, string Status, long VendorId, string? VendorName, string RefNo, DateOnly InvoiceDt,
    DateOnly DueDt, string? Terms, string CurrencyCode, decimal ExchangeRate, bool TaxInvoiceReceived, string? Remarks,
    decimal SupplyAmt, decimal TaxAmt, decimal TotAmt, decimal TotBaseAmt, decimal ClsAmt, decimal BalAmt, long? VendorReturnId,
    DateTimeOffset UpdatedAt, DateTimeOffset? CreatedAt, string? CreatedBy, string? UpdatedBy);

/// <summary>mm_supplier_invoice_line row with the codes it names and the receipt line it matches.</summary>
public sealed record SupplierInvoiceLineRow(
    long Id, int Seq, long? GoodsReceiptLineId, string? GrNo, int? GrSeq, long ItemId, string? ItemCd, string? ItemName, string? Unit,
    decimal? GrQty, decimal? GrPrc, decimal Qty, decimal Prc, long TaxCodeId, string? TaxCode, decimal SupplyAmt, decimal TaxAmt,
    bool Deductible, decimal PvAmt)
{
    public StoredSupplierInvoiceLine ToStored() =>
        new(Id, Seq, GoodsReceiptLineId ?? 0, ItemId, Qty, Prc, TaxCodeId, Deductible, PvAmt);
}

public sealed record SupplierInvoiceVendorRow(long Id, string? VName, string? Terms);

/// <summary>A receipt line the invoice can import, with the PO line's price and discount (MM-BR-763).</summary>
public sealed record OpenReceiptLineRow(
    long GoodsReceiptLineId, long GoodsReceiptId, string GrNo, int GrSeq, DateOnly GrDt, long ItemId, string? ItemCd, string? ItemName,
    string? Unit, decimal GrQty, decimal ReturnedQty, decimal InvoicedQty, decimal OpenQty, decimal GrPrc, decimal? PoPrc,
    decimal? PoDcRate, long TaxCodeId, string? TaxCode, bool Deductible, string CurrencyCode, decimal ExchangeRate);

/// <summary>Header values to write; Lines are every line with its computed amounts (Id null for a new line).</summary>
public sealed record SupplierInvoiceWrite(
    SupplierInvoiceHeader Header, SupplierInvoiceTotals Totals, decimal BalAmt, string Status, IReadOnlyList<SupplierInvoiceLine> Lines);

/// <summary>Supplier invoices (mm_supplier_invoice, mm_supplier_invoice_line, MM-UC-032). Callers pass their own transaction.</summary>
public interface ISupplierInvoiceRepository
{
    /// <summary>Newest invoice date first.</summary>
    Task<IReadOnlyList<SupplierInvoiceSummaryDto>> SearchAsync(
        SupplierInvoiceSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="forUpdate">Locks the header row until the transaction ends.</param>
    Task<SupplierInvoiceRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Lines in seq order.</summary>
    Task<IReadOnlyList<SupplierInvoiceLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<SupplierInvoiceVendorRow?> FindVendorAsync(long vendorId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the goods receipts of the given receipt lines in id order and reads those lines with their receipt, tax kind and PO
    /// price (MM-BR-753, MM-BR-757). Lines that do not exist or are not item receipts are left out.
    /// </summary>
    Task<IReadOnlyList<InvoiceReceiptLine>> LockReceiptLinesAsync(
        IReadOnlyCollection<long> goodsReceiptLineIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>MM-BR-764: another invoice of the vendor that is not void has this vendor invoice number.</summary>
    Task<bool> RefNoExistsAsync(long vendorId, string refNo, long? exceptId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>The new header id.</returns>
    Task<long> InsertAsync(
        string invoiceNo, long businessPlaceId, SupplierInvoiceWrite invoice, string user, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the header, deletes the given lines, writes the supply and tax of the saved lines (computed again with the whole
    /// document) and inserts the new lines (Id null).
    /// </summary>
    /// <returns>False when the header changed after updatedAt.</returns>
    Task<bool> UpdateAsync(
        long id, SupplierInvoiceWrite invoice, IReadOnlyCollection<long> deletedLineIds, DateTimeOffset updatedAt, string user,
        IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Adds to mm_goods_receipt_line.invoiced_qty (negative to take back, MM-BR-757).</summary>
    Task AddInvoicedQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByReceiptLine, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Original PV movements of the given lines (ref_key = invoice number, ref_seq = line) not reversed yet (MM-BR-760).</summary>
    Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(
        string invoiceNo, IReadOnlyCollection<int> seqs, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when the header changed after updatedAt.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// MM-BR-763: item receipt lines of the vendor (in the currency, when given) with open qty above 0, by receipt date, receipt
    /// number and line. Lines the invoice excludeInvoiceId already has are left out.
    /// </summary>
    Task<IReadOnlyList<OpenReceiptLineRow>> FindOpenReceiptLinesAsync(
        long vendorId, string? currencyCode, long? excludeInvoiceId, IDbTransaction transaction, CancellationToken cancellationToken);
}

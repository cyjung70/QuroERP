using System.Data;
using Dapper;
using QuroErp.Application.Mm.SupplierInvoices;
using QuroErp.Contracts.Mm.SupplierInvoices;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.SupplierInvoices;

namespace QuroErp.Infrastructure.Mm.SupplierInvoices;

internal sealed class SupplierInvoiceRepository : ISupplierInvoiceRepository
{
    private const string HeaderColumns = """
        s.id, s.invoice_no, s.type, s.status, s.vendor_id, v.v_name AS vendor_name, s.ref_no, s.invoice_dt, s.due_dt, s.terms,
        s.currency_code, s.exchange_rate, s.tax_invoice_received, s.remarks, s.supply_amt, s.tax_amt, s.tot_amt, s.tot_base_amt,
        s.cls_amt, s.bal_amt, s.vendor_return_id, s.updated_at, s.created_at, s.created_by, s.updated_by
        """;

    private const string Search = """
        SELECT s.id, s.invoice_no, s.type, s.status, s.invoice_dt, s.due_dt, s.vendor_id, v.v_name AS vendor_name, s.ref_no,
               s.currency_code, s.tot_amt, s.bal_amt
        FROM mm_supplier_invoice s LEFT JOIN bc_vendor v ON v.id = s.vendor_id
        WHERE (@InvoiceNo::varchar IS NULL OR starts_with(s.invoice_no, @InvoiceNo))
          AND (@VendorId::bigint IS NULL OR s.vendor_id = @VendorId)
          AND (@FromDt::date IS NULL OR s.invoice_dt >= @FromDt)
          AND (@ToDt::date IS NULL OR s.invoice_dt <= @ToDt)
          AND (@Type::varchar IS NULL OR s.type = @Type)
          AND (@Status::varchar IS NULL OR s.status = @Status)
        ORDER BY s.invoice_dt DESC, s.invoice_no DESC
        LIMIT @Limit
        """;

    private const string Find = $"""
        SELECT {HeaderColumns}
        FROM mm_supplier_invoice s LEFT JOIN bc_vendor v ON v.id = s.vendor_id
        WHERE s.id = @Id
        """;

    private const string Lock = "SELECT id FROM mm_supplier_invoice WHERE id = @Id FOR UPDATE";

    private const string SelectLines = """
        SELECT l.id, l.seq, l.goods_receipt_line_id, g.gr_no, gl.seq AS gr_seq, l.item_id, i.item_cd,
               coalesce(gl.item_name, i.item_name) AS item_name, i.unit, gl.gr_qty, gl.gr_prc, l.qty, l.prc, l.tax_code_id, t.tax_code,
               l.supply_amt, l.tax_amt, l.deductible, l.pv_amt
        FROM mm_supplier_invoice_line l
        LEFT JOIN mm_goods_receipt_line gl ON gl.id = l.goods_receipt_line_id
        LEFT JOIN mm_goods_receipt g ON g.id = gl.goods_receipt_id
        LEFT JOIN bc_item i ON i.id = l.item_id
        LEFT JOIN bc_tax_code t ON t.id = l.tax_code_id
        WHERE l.supplier_invoice_id = @Id
        ORDER BY l.seq
        """;

    private const string SelectVendor = "SELECT id, v_name, terms FROM bc_vendor WHERE id = @Id";

    // Receipts first, in id order, so two invoices (or an invoice and a receipt save) cannot deadlock
    private const string LockReceipts = """
        SELECT g.id FROM mm_goods_receipt g
        WHERE g.id IN (SELECT goods_receipt_id FROM mm_goods_receipt_line WHERE id = ANY(@Ids))
        ORDER BY g.id
        FOR UPDATE
        """;

    // Only item receipts; expense receipts are not part of the MVP. Migrated rows may lack a vendor or amounts
    private const string SelectReceiptLines = """
        SELECT gl.id, g.gr_no, gl.seq, coalesce(g.vendor_id, 0) AS vendor_id, g.currency_code, g.exchange_rate,
               coalesce(gl.item_id, 0) AS item_id, i.type AS item_type, gl.warehouse_id, gl.lot_id, coalesce(gl.gr_qty, 0) AS gr_qty,
               coalesce(gl.gr_prc, 0) AS gr_prc, gl.tax_code_id, t.tax_kind, gl.supply_amt, gl.tax_amt, gl.deductible, gl.invoiced_qty,
               gl.returned_qty, pl.po_prc, pl.dc_rate AS po_dc_rate
        FROM mm_goods_receipt_line gl
        JOIN mm_goods_receipt g ON g.id = gl.goods_receipt_id
        JOIN bc_tax_code t ON t.id = gl.tax_code_id
        LEFT JOIN bc_item i ON i.id = gl.item_id
        LEFT JOIN mm_purchase_order_line pl ON pl.id = gl.purchase_order_line_id
        WHERE gl.id = ANY(@Ids) AND coalesce(g.type, 'ITEM') = 'ITEM'
        """;

    private const string SelectRefNoExists = """
        SELECT EXISTS (SELECT 1 FROM mm_supplier_invoice
                       WHERE vendor_id = @VendorId AND ref_no = @RefNo AND status <> 'VO' AND (@ExceptId::bigint IS NULL OR id <> @ExceptId))
        """;

    private const string InsertHeader = """
        INSERT INTO mm_supplier_invoice (invoice_no, type, vendor_id, business_place_id, ref_no, invoice_dt, due_dt, terms, currency_code,
                                         exchange_rate, supply_amt, tax_amt, tot_amt, tot_base_amt, bal_amt, status, tax_invoice_received,
                                         remarks, created_by, updated_by)
        VALUES (@InvoiceNo, 'INV', @VendorId, @BusinessPlaceId, @RefNo, @InvoiceDt, @DueDt, @Terms, @CurrencyCode, @ExchangeRate,
                @SupplyAmt, @TaxAmt, @TotAmt, @TotBaseAmt, @BalAmt, @Status, @TaxInvoiceReceived, @Remarks, @User, @User)
        RETURNING id
        """;

    private const string UpdateHeader = """
        UPDATE mm_supplier_invoice
        SET vendor_id = @VendorId, ref_no = @RefNo, invoice_dt = @InvoiceDt, due_dt = @DueDt, terms = @Terms, currency_code = @CurrencyCode,
            exchange_rate = @ExchangeRate, supply_amt = @SupplyAmt, tax_amt = @TaxAmt, tot_amt = @TotAmt, tot_base_amt = @TotBaseAmt,
            bal_amt = @BalAmt, status = @Status, tax_invoice_received = @TaxInvoiceReceived, remarks = @Remarks, updated_at = now(),
            updated_by = @User
        WHERE id = @Id AND updated_at = @UpdatedAt
        """;

    private const string DeleteLines = "DELETE FROM mm_supplier_invoice_line WHERE supplier_invoice_id = @Id AND id = ANY(@Ids)";

    private const string UpdateLineAmounts = """
        UPDATE mm_supplier_invoice_line SET supply_amt = @SupplyAmt, tax_amt = @TaxAmt WHERE id = @LineId AND supplier_invoice_id = @Id
        """;

    private const string InsertLine = """
        INSERT INTO mm_supplier_invoice_line (supplier_invoice_id, seq, goods_receipt_line_id, item_id, qty, prc, tax_code_id, supply_amt,
                                              tax_amt, deductible, pv_amt, created_by, updated_by)
        VALUES (@Id, @Seq, @GoodsReceiptLineId, @ItemId, @Qty, @Prc, @TaxCodeId, @SupplyAmt, @TaxAmt, @Deductible, @PvAmt, @User, @User)
        """;

    private const string AddInvoicedQty = """
        UPDATE mm_goods_receipt_line SET invoiced_qty = invoiced_qty + @Qty, updated_at = now(), updated_by = @User WHERE id = @Id
        """;

    private const string SelectOpenMovements = """
        SELECT m.id FROM mm_goods_movement m
        WHERE m.mov_type = @MovType AND m.ref_key = @InvoiceNo AND m.ref_seq = ANY(@Seqs) AND m.parent_movement_id IS NULL
          AND NOT EXISTS (SELECT 1 FROM mm_goods_movement r WHERE r.parent_movement_id = m.id)
        ORDER BY m.id
        """;

    // Lines go with the header (ON DELETE CASCADE)
    private const string Delete = "DELETE FROM mm_supplier_invoice WHERE id = @Id AND updated_at = @UpdatedAt";

    private const string SelectOpenReceiptLines = """
        SELECT gl.id AS goods_receipt_line_id, g.id AS goods_receipt_id, g.gr_no, gl.seq AS gr_seq, coalesce(g.gr_dt, g.created_at::date) AS gr_dt,
               coalesce(gl.item_id, 0) AS item_id, i.item_cd, coalesce(gl.item_name, i.item_name) AS item_name, i.unit,
               coalesce(gl.gr_qty, 0) AS gr_qty, gl.returned_qty, gl.invoiced_qty,
               coalesce(gl.gr_qty, 0) - gl.returned_qty - gl.invoiced_qty AS open_qty, coalesce(gl.gr_prc, 0) AS gr_prc, pl.po_prc,
               pl.dc_rate AS po_dc_rate, gl.tax_code_id, t.tax_code, gl.deductible, g.currency_code, g.exchange_rate
        FROM mm_goods_receipt_line gl
        JOIN mm_goods_receipt g ON g.id = gl.goods_receipt_id
        LEFT JOIN bc_item i ON i.id = gl.item_id
        LEFT JOIN bc_tax_code t ON t.id = gl.tax_code_id
        LEFT JOIN mm_purchase_order_line pl ON pl.id = gl.purchase_order_line_id
        WHERE g.vendor_id = @VendorId AND coalesce(g.type, 'ITEM') = 'ITEM'
          AND (@CurrencyCode::varchar IS NULL OR g.currency_code = @CurrencyCode)
          AND coalesce(gl.gr_qty, 0) - gl.returned_qty - gl.invoiced_qty > 0
          AND (@ExcludeInvoiceId::bigint IS NULL OR NOT EXISTS (
                SELECT 1 FROM mm_supplier_invoice_line x WHERE x.supplier_invoice_id = @ExcludeInvoiceId AND x.goods_receipt_line_id = gl.id))
        ORDER BY gr_dt, g.gr_no, gl.seq
        """;

    public async Task<IReadOnlyList<SupplierInvoiceSummaryDto>> SearchAsync(
        SupplierInvoiceSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<SupplierInvoiceSummaryDto>(new CommandDefinition(
            Search,
            new
            {
                InvoiceNo = Upper(query.InvoiceNo),
                query.VendorId,
                query.FromDt,
                query.ToDt,
                Type = Upper(query.Type),
                Status = Upper(query.Status),
                Limit = limit,
            },
            transaction, cancellationToken: cancellationToken))];

    public async Task<SupplierInvoiceRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(Lock, new { Id = id }, transaction, cancellationToken: cancellationToken));
        }
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Find, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToSupplierInvoiceRow();
    }

    public async Task<IReadOnlyList<SupplierInvoiceLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<SupplierInvoiceLineRow>(
            new CommandDefinition(SelectLines, new { Id = id }, transaction, cancellationToken: cancellationToken))];

    public Task<SupplierInvoiceVendorRow?> FindVendorAsync(long vendorId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<SupplierInvoiceVendorRow>(
            new CommandDefinition(SelectVendor, new { Id = vendorId }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<InvoiceReceiptLine>> LockReceiptLinesAsync(
        IReadOnlyCollection<long> goodsReceiptLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (goodsReceiptLineIds.Count == 0)
        {
            return [];
        }
        var ids = goodsReceiptLineIds.ToArray();
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(LockReceipts, new { Ids = ids }, transaction, cancellationToken: cancellationToken));
        return [.. await transaction.Connection!.QueryAsync<InvoiceReceiptLine>(
            new CommandDefinition(SelectReceiptLines, new { Ids = ids }, transaction, cancellationToken: cancellationToken))];
    }

    public Task<bool> RefNoExistsAsync(long vendorId, string refNo, long? exceptId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(new CommandDefinition(
            SelectRefNoExists, new { VendorId = vendorId, RefNo = refNo, ExceptId = exceptId }, transaction, cancellationToken: cancellationToken));

    public async Task<long> InsertAsync(
        string invoiceNo, long businessPlaceId, SupplierInvoiceWrite invoice, string user, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var id = await transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            InsertHeader, HeaderParameters(invoice, user, new { InvoiceNo = invoiceNo, BusinessPlaceId = businessPlaceId }), transaction,
            cancellationToken: cancellationToken));
        await InsertLinesAsync(id, invoice.Lines, user, transaction, cancellationToken);
        return id;
    }

    public async Task<bool> UpdateAsync(
        long id, SupplierInvoiceWrite invoice, IReadOnlyCollection<long> deletedLineIds, DateTimeOffset updatedAt, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        if (await connection.ExecuteAsync(new CommandDefinition(
                UpdateHeader, HeaderParameters(invoice, user, new { Id = id, UpdatedAt = updatedAt }), transaction,
                cancellationToken: cancellationToken)) == 0)
        {
            return false;
        }
        await connection.ExecuteAsync(new CommandDefinition(
            DeleteLines, new { Id = id, Ids = deletedLineIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            UpdateLineAmounts,
            invoice.Lines.Where(l => l.Id is not null).Select(l => new { Id = id, LineId = l.Id, l.SupplyAmt, l.TaxAmt }).ToList(),
            transaction, cancellationToken: cancellationToken));
        await InsertLinesAsync(id, invoice.Lines.Where(l => l.Id is null), user, transaction, cancellationToken);
        return true;
    }

    public Task AddInvoicedQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByReceiptLine, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            AddInvoicedQty, qtyByReceiptLine.Select(e => new { Id = e.Key, Qty = e.Value, User = user }).ToList(), transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(
        string invoiceNo, IReadOnlyCollection<int> seqs, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            SelectOpenMovements, new { MovType = MovementType.PriceVariance, InvoiceNo = invoiceNo, Seqs = seqs.ToArray() }, transaction,
            cancellationToken: cancellationToken))];

    public async Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Id = id, UpdatedAt = updatedAt }, transaction, cancellationToken: cancellationToken)) == 1;

    public async Task<IReadOnlyList<OpenReceiptLineRow>> FindOpenReceiptLinesAsync(
        long vendorId, string? currencyCode, long? excludeInvoiceId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<OpenReceiptLineRow>(new CommandDefinition(
            SelectOpenReceiptLines, new { VendorId = vendorId, CurrencyCode = currencyCode, ExcludeInvoiceId = excludeInvoiceId }, transaction,
            cancellationToken: cancellationToken))];

    private static async Task InsertLinesAsync(
        long id, IEnumerable<SupplierInvoiceLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var l in lines)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                InsertLine,
                new
                {
                    Id = id,
                    l.Seq,
                    l.GoodsReceiptLineId,
                    l.ItemId,
                    l.Qty,
                    l.Prc,
                    l.TaxCodeId,
                    l.SupplyAmt,
                    l.TaxAmt,
                    l.Deductible,
                    l.PvAmt,
                    User = user,
                },
                transaction, cancellationToken: cancellationToken));
        }
    }

    private static DynamicParameters HeaderParameters(SupplierInvoiceWrite invoice, string user, object keys)
    {
        var h = invoice.Header;
        var t = invoice.Totals;
        var parameters = new DynamicParameters(new
        {
            h.VendorId,
            h.RefNo,
            h.InvoiceDt,
            h.DueDt,
            h.Terms,
            h.CurrencyCode,
            h.ExchangeRate,
            h.TaxInvoiceReceived,
            h.Remarks,
            t.SupplyAmt,
            t.TaxAmt,
            t.TotAmt,
            t.TotBaseAmt,
            invoice.BalAmt,
            invoice.Status,
            User = user,
        });
        parameters.AddDynamicParams(keys);
        return parameters;
    }

    private static string? Upper(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id, string InvoiceNo, string Type, string Status, long VendorId, string? VendorName, string RefNo, DateOnly InvoiceDt,
        DateOnly DueDt, string? Terms, string CurrencyCode, decimal ExchangeRate, bool TaxInvoiceReceived, string? Remarks,
        decimal SupplyAmt, decimal TaxAmt, decimal TotAmt, decimal TotBaseAmt, decimal ClsAmt, decimal BalAmt, long? VendorReturnId,
        DateTime UpdatedAt, DateTime? CreatedAt, string? CreatedBy, string? UpdatedBy)
    {
        public SupplierInvoiceRow ToSupplierInvoiceRow() => new(
            Id, InvoiceNo, Type, Status, VendorId, VendorName, RefNo, InvoiceDt, DueDt, Terms, CurrencyCode, ExchangeRate, TaxInvoiceReceived,
            Remarks, SupplyAmt, TaxAmt, TotAmt, TotBaseAmt, ClsAmt, BalAmt, VendorReturnId, Utc(UpdatedAt), Utc(CreatedAt), CreatedBy,
            UpdatedBy);

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

        private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;
    }
}

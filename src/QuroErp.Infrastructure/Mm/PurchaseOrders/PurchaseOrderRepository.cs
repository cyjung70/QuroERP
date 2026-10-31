using System.Data;
using Dapper;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Domain.Mm.PurchaseOrders;

namespace QuroErp.Infrastructure.Mm.PurchaseOrders;

internal sealed class PurchaseOrderRepository : IPurchaseOrderRepository
{
    // Migrated rows may lack a status, date or amounts; they read as approved (MM-BR-019) with zero amounts
    private const string HeaderColumns = """
        p.id, p.po_no, coalesce(p.status, 'PO') AS status, coalesce(p.vendor_id, 0) AS vendor_id, v.v_name AS vendor_name,
        coalesce(p.po_dt, p.created_at::date) AS po_dt, p.currency_code, p.exchange_rate, p.incoterms, p.due_dt, p.warehouse_id,
        p.payment_terms, p.buyer, p.remarks, p.supply_amt, p.tax_amt, coalesce(p.tot_po_amt, 0) AS tot_po_amt, p.tot_base_amt,
        p.revision_no, p.approved_at, p.approved_by, p.closed_at, p.closed_by, p.cancelled_at, p.cancelled_by,
        coalesce((SELECT sum(g.gr_qty)
                  FROM mm_goods_receipt_line g JOIN mm_purchase_order_line l ON l.id = g.purchase_order_line_id
                  WHERE l.purchase_order_id = p.id), 0) AS received_qty,
        p.updated_at
        """;

    private const string Search = $"""
        SELECT {HeaderColumns}
        FROM mm_purchase_order p LEFT JOIN bc_vendor v ON v.id = p.vendor_id
        WHERE (@PoNo::varchar IS NULL OR starts_with(p.po_no, @PoNo))
          AND (@VendorId::bigint IS NULL OR p.vendor_id = @VendorId)
          AND (@Status::varchar IS NULL OR coalesce(p.status, 'PO') = @Status)
          AND (@FromDt::date IS NULL OR p.po_dt >= @FromDt)
          AND (@ToDt::date IS NULL OR p.po_dt <= @ToDt)
        ORDER BY p.po_dt DESC NULLS LAST, p.po_no DESC
        LIMIT @Limit
        """;

    private const string Find = $"""
        SELECT {HeaderColumns}
        FROM mm_purchase_order p LEFT JOIN bc_vendor v ON v.id = p.vendor_id
        WHERE p.id = @Id
        """;

    private const string Lock = "SELECT id FROM mm_purchase_order WHERE id = @Id FOR UPDATE";

    private const string SelectLines = """
        SELECT l.id, l.seq, coalesce(l.item_id, 0) AS item_id, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name,
               coalesce(l.po_qty, 0) AS po_qty, coalesce(l.po_prc, 0) AS po_prc, l.gross_amt, l.dc_rate, l.dc_amt,
               coalesce(l.po_amt, 0) AS po_amt, l.tax_code_id, l.supply_amt, l.tax_amt, l.due_dt, l.warehouse_id,
               coalesce((SELECT sum(g.gr_qty) FROM mm_goods_receipt_line g WHERE g.purchase_order_line_id = l.id), 0) AS received_qty
        FROM mm_purchase_order_line l LEFT JOIN bc_item i ON i.id = l.item_id
        WHERE l.purchase_order_id = @Id
        ORDER BY l.seq
        """;

    private const string SelectVendor = "SELECT id, v_name, currency_code, tax_code_id FROM bc_vendor WHERE id = @Id";

    private const string SelectItemTypes = "SELECT id, type AS item_type FROM bc_item WHERE id = ANY(@Ids) AND type IS NOT NULL";

    private const string SelectUsableWarehouses = "SELECT id FROM bc_warehouse WHERE id = ANY(@Ids) AND use_yn";

    private const string InsertHeader = """
        INSERT INTO mm_purchase_order (po_no, status, vendor_id, po_dt, currency_code, exchange_rate, incoterms, due_dt, warehouse_id,
                                       payment_terms, buyer, remarks, supply_amt, tax_amt, tot_po_amt, tot_base_amt, revision_no,
                                       created_by, updated_by)
        VALUES (@PoNo, @Status, @VendorId, @PoDt, @CurrencyCode, @ExchangeRate, @Incoterms, @DueDt, @WarehouseId, @PaymentTerms, @Buyer,
                @Remarks, @SupplyAmt, @TaxAmt, @TotPoAmt, @TotBaseAmt, @RevisionNo, @User, @User)
        RETURNING id
        """;

    private const string UpdateHeader = """
        UPDATE mm_purchase_order
        SET status = @Status, vendor_id = @VendorId, po_dt = @PoDt, currency_code = @CurrencyCode, exchange_rate = @ExchangeRate,
            incoterms = @Incoterms, due_dt = @DueDt, warehouse_id = @WarehouseId, payment_terms = @PaymentTerms, buyer = @Buyer,
            remarks = @Remarks, supply_amt = @SupplyAmt, tax_amt = @TaxAmt, tot_po_amt = @TotPoAmt, tot_base_amt = @TotBaseAmt,
            revision_no = @RevisionNo, updated_at = now(), updated_by = @User
        WHERE id = @Id AND updated_at = @UpdatedAt
        """;

    private const string DeleteMissingLines = """
        DELETE FROM mm_purchase_order_line WHERE purchase_order_id = @Id AND NOT (id = ANY(@KeepIds))
        """;

    // Kept lines are renumbered; moving them out of the way first keeps (purchase_order_id, seq) unique
    private const string MoveSeqAside = "UPDATE mm_purchase_order_line SET seq = -seq WHERE purchase_order_id = @Id";

    private const string UpdateLine = """
        UPDATE mm_purchase_order_line
        SET seq = @Seq, item_id = @ItemId, item_name = @ItemName, po_qty = @PoQty, po_prc = @PoPrc, gross_amt = @GrossAmt,
            dc_rate = @DcRate, dc_amt = @DcAmt, po_amt = @PoAmt, tax_code_id = @TaxCodeId, supply_amt = @SupplyAmt, tax_amt = @TaxAmt,
            due_dt = @DueDt, warehouse_id = @WarehouseId, updated_at = now(), updated_by = @User
        WHERE id = @LineId AND purchase_order_id = @Id
        """;

    private const string InsertLine = """
        INSERT INTO mm_purchase_order_line (purchase_order_id, seq, item_id, item_name, po_qty, po_prc, gross_amt, dc_rate, dc_amt, po_amt,
                                            tax_code_id, supply_amt, tax_amt, due_dt, warehouse_id, created_by, updated_by)
        VALUES (@Id, @Seq, @ItemId, @ItemName, @PoQty, @PoPrc, @GrossAmt, @DcRate, @DcAmt, @PoAmt, @TaxCodeId, @SupplyAmt, @TaxAmt,
                @DueDt, @WarehouseId, @User, @User)
        """;

    private const string SetStatus = """
        UPDATE mm_purchase_order
        SET status = @Status,
            approved_at = CASE WHEN @Approved THEN now() ELSE approved_at END,
            approved_by = CASE WHEN @Approved THEN @User ELSE approved_by END,
            closed_at = CASE WHEN @Closed THEN now() ELSE closed_at END,
            closed_by = CASE WHEN @Closed THEN @User ELSE closed_by END,
            cancelled_at = CASE WHEN @Cancelled THEN now() ELSE cancelled_at END,
            cancelled_by = CASE WHEN @Cancelled THEN @User ELSE cancelled_by END,
            updated_at = now(), updated_by = @User
        WHERE id = @Id AND updated_at = @UpdatedAt
        """;

    // Lines go with the header (ON DELETE CASCADE)
    private const string Delete = "DELETE FROM mm_purchase_order WHERE id = @Id AND updated_at = @UpdatedAt";

    public async Task<IReadOnlyList<PurchaseOrderRow>> SearchAsync(
        PurchaseOrderSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<Row>(new CommandDefinition(
            Search,
            new
            {
                PoNo = string.IsNullOrWhiteSpace(query.PoNo) ? null : query.PoNo.Trim(),
                query.VendorId,
                Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status,
                query.FromDt,
                query.ToDt,
                Limit = limit,
            },
            transaction, cancellationToken: cancellationToken))).Select(r => r.ToPurchaseOrderRow()).ToList();

    public async Task<PurchaseOrderRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(Lock, new { Id = id }, transaction, cancellationToken: cancellationToken));
        }
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Find, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToPurchaseOrderRow();
    }

    public async Task<IReadOnlyList<PurchaseOrderLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<PurchaseOrderLineRow>(
            new CommandDefinition(SelectLines, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    public Task<PurchaseOrderVendorRow?> FindVendorAsync(long vendorId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<PurchaseOrderVendorRow>(
            new CommandDefinition(SelectVendor, new { Id = vendorId }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyDictionary<long, string>> FindItemTypesAsync(
        IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string ItemType)>(new CommandDefinition(
            SelectItemTypes, new { Ids = itemIds.ToArray() }, transaction, cancellationToken: cancellationToken)))
        .ToDictionary(r => r.Id, r => r.ItemType);

    public async Task<IReadOnlySet<long>> FindUsableWarehousesAsync(
        IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            SelectUsableWarehouses, new { Ids = warehouseIds.ToArray() }, transaction, cancellationToken: cancellationToken)))
        .ToHashSet();

    public async Task<long> InsertAsync(
        string poNo, PurchaseOrderWrite order, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var id = await transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            InsertHeader, HeaderParameters(order, user, new { PoNo = poNo }), transaction, cancellationToken: cancellationToken));
        await InsertLinesAsync(id, order.Lines, user, transaction, cancellationToken);
        return id;
    }

    public async Task<bool> UpdateAsync(
        long id, PurchaseOrderWrite order, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        if (await connection.ExecuteAsync(new CommandDefinition(
                UpdateHeader, HeaderParameters(order, user, new { Id = id, UpdatedAt = updatedAt }), transaction,
                cancellationToken: cancellationToken)) == 0)
        {
            return false;
        }
        var kept = order.Lines.Where(l => l.Id is not null).ToList();
        await connection.ExecuteAsync(new CommandDefinition(
            DeleteMissingLines, new { Id = id, KeepIds = kept.Select(l => l.Id!.Value).ToArray() }, transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(MoveSeqAside, new { Id = id }, transaction, cancellationToken: cancellationToken));
        foreach (var line in kept)
        {
            // A line id of another order means the screen is stale
            if (await connection.ExecuteAsync(new CommandDefinition(
                    UpdateLine, LineParameters(id, line, user), transaction, cancellationToken: cancellationToken)) == 0)
            {
                return false;
            }
        }
        await InsertLinesAsync(id, order.Lines.Where(l => l.Id is null), user, transaction, cancellationToken);
        return true;
    }

    public async Task<bool> SetStatusAsync(
        long id, PoStatus status, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SetStatus,
            new
            {
                Id = id,
                Status = status.ToCode(),
                Approved = status == PoStatus.Approved,
                Closed = status == PoStatus.Closed,
                Cancelled = status == PoStatus.Cancelled,
                UpdatedAt = updatedAt,
                User = user,
            },
            transaction, cancellationToken: cancellationToken)) == 1;

    public async Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Id = id, UpdatedAt = updatedAt }, transaction, cancellationToken: cancellationToken)) == 1;

    private static async Task InsertLinesAsync(
        long id, IEnumerable<PurchaseOrderLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                InsertLine, LineParameters(id, line, user), transaction, cancellationToken: cancellationToken));
        }
    }

    private static DynamicParameters HeaderParameters(PurchaseOrderWrite order, string user, object keys)
    {
        var h = order.Header;
        var t = order.Totals;
        var parameters = new DynamicParameters(new
        {
            Status = order.Status.ToCode(),
            h.VendorId,
            h.PoDt,
            h.CurrencyCode,
            h.ExchangeRate,
            h.Incoterms,
            h.DueDt,
            h.WarehouseId,
            h.PaymentTerms,
            h.Buyer,
            h.Remarks,
            t.SupplyAmt,
            t.TaxAmt,
            t.TotPoAmt,
            t.TotBaseAmt,
            order.RevisionNo,
            User = user,
        });
        parameters.AddDynamicParams(keys);
        return parameters;
    }

    private static object LineParameters(long id, PurchaseOrderLine l, string user) => new
    {
        Id = id,
        LineId = l.Id,
        l.Seq,
        l.ItemId,
        l.ItemName,
        l.PoQty,
        l.PoPrc,
        l.GrossAmt,
        l.DcRate,
        l.DcAmt,
        l.PoAmt,
        l.TaxCodeId,
        l.SupplyAmt,
        l.TaxAmt,
        l.DueDt,
        l.WarehouseId,
        User = user,
    };

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id, string PoNo, string Status, long VendorId, string? VendorName, DateOnly PoDt, string CurrencyCode, decimal ExchangeRate,
        string? Incoterms, DateOnly? DueDt, long? WarehouseId, string? PaymentTerms, string? Buyer, string? Remarks, decimal SupplyAmt,
        decimal TaxAmt, decimal TotPoAmt, decimal TotBaseAmt, int RevisionNo, DateTime? ApprovedAt, string? ApprovedBy,
        DateTime? ClosedAt, string? ClosedBy, DateTime? CancelledAt, string? CancelledBy, decimal ReceivedQty, DateTime UpdatedAt)
    {
        public PurchaseOrderRow ToPurchaseOrderRow() => new(
            Id, PoNo, Status, VendorId, VendorName, PoDt, CurrencyCode, ExchangeRate, Incoterms, DueDt, WarehouseId, PaymentTerms, Buyer,
            Remarks, SupplyAmt, TaxAmt, TotPoAmt, TotBaseAmt, RevisionNo, Utc(ApprovedAt), ApprovedBy, Utc(ClosedAt), ClosedBy,
            Utc(CancelledAt), CancelledBy, ReceivedQty, Utc(UpdatedAt));

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

        private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;
    }
}

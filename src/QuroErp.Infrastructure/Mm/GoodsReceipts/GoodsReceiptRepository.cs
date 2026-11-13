using System.Data;
using Dapper;
using QuroErp.Application.Mm.GoodsReceipts;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.PurchaseOrders;

using QuroErp.Infrastructure.Mm.Inventory;

namespace QuroErp.Infrastructure.Mm.GoodsReceipts;

internal sealed class GoodsReceiptRepository : IGoodsReceiptRepository
{
    // Migrated rows may lack a vendor, date or amounts; they read as zero
    private const string HeaderColumns = """
        g.id, g.gr_no, coalesce(g.vendor_id, 0) AS vendor_id, v.v_name AS vendor_name, coalesce(g.gr_dt, g.created_at::date) AS gr_dt,
        g.warehouse_id, g.ref_no, g.remarks, g.currency_code, g.exchange_rate, g.supply_amt, g.tax_amt,
        coalesce(g.tot_gr_amt, 0) AS tot_gr_amt, g.tot_base_amt, g.updated_at, g.created_at, g.created_by, g.updated_by
        """;

    // Only item receipts; expense receipts (type EXPENSE) are not part of the MVP
    private const string Search = """
        SELECT g.id, g.gr_no, coalesce(g.gr_dt, g.created_at::date) AS gr_dt, coalesce(g.vendor_id, 0) AS vendor_id, v.v_name AS vendor_name,
               g.currency_code, coalesce(g.tot_gr_amt, 0) AS tot_gr_amt,
               EXISTS (SELECT 1 FROM mm_goods_receipt_line l WHERE l.goods_receipt_id = g.id AND l.invoiced_qty <> 0) AS any_invoiced,
               NOT EXISTS (SELECT 1 FROM mm_goods_receipt_line l
                           WHERE l.goods_receipt_id = g.id
                             AND coalesce(l.gr_qty, 0) - l.returned_qty - l.invoiced_qty <> 0) AS all_invoiced
        FROM mm_goods_receipt g LEFT JOIN bc_vendor v ON v.id = g.vendor_id
        WHERE coalesce(g.type, 'ITEM') = 'ITEM'
          AND (@GrNo::varchar IS NULL OR starts_with(g.gr_no, @GrNo))
          AND (@VendorId::bigint IS NULL OR g.vendor_id = @VendorId)
          AND (@FromDt::date IS NULL OR g.gr_dt >= @FromDt)
          AND (@ToDt::date IS NULL OR g.gr_dt <= @ToDt)
        ORDER BY g.gr_dt DESC NULLS LAST, g.gr_no DESC
        LIMIT @Limit
        """;

    private const string Find = $"""
        SELECT {HeaderColumns}
        FROM mm_goods_receipt g LEFT JOIN bc_vendor v ON v.id = g.vendor_id
        WHERE g.id = @Id
        """;

    private const string Lock = "SELECT id FROM mm_goods_receipt WHERE id = @Id FOR UPDATE";

    private const string SelectLines = """
        SELECT l.id, l.seq, coalesce(l.item_id, 0) AS item_id, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name, i.unit,
               l.warehouse_id, w.warehouse_code, l.lot_id, lot.lot_no, coalesce(l.gr_qty, 0) AS gr_qty, coalesce(l.gr_prc, 0) AS gr_prc,
               coalesce(l.gr_amt, 0) AS gr_amt, l.tax_code_id, t.tax_code, l.supply_amt, l.tax_amt, l.deductible,
               l.purchase_order_line_id, p.po_no, pl.seq AS po_seq, pl.po_qty, pl.receipt_qty AS po_received_qty,
               l.invoiced_qty, l.returned_qty
        FROM mm_goods_receipt_line l
        LEFT JOIN bc_item i ON i.id = l.item_id
        LEFT JOIN bc_warehouse w ON w.id = l.warehouse_id
        LEFT JOIN mm_lot lot ON lot.id = l.lot_id
        LEFT JOIN bc_tax_code t ON t.id = l.tax_code_id
        LEFT JOIN mm_purchase_order_line pl ON pl.id = l.purchase_order_line_id
        LEFT JOIN mm_purchase_order p ON p.id = pl.purchase_order_id
        WHERE l.goods_receipt_id = @Id
        ORDER BY l.seq
        """;

    private const string SelectVendor = "SELECT id, v_name, currency_code, tax_code_id FROM bc_vendor WHERE id = @Id";

    private const string SelectItemTypes = "SELECT id, type AS item_type FROM bc_item WHERE id = ANY(@Ids) AND type IS NOT NULL";

    private const string SelectUsableWarehouses = "SELECT id FROM bc_warehouse WHERE id = ANY(@Ids) AND use_yn";

    private const string SelectDefaultWarehouse = """
        SELECT coalesce(
            (SELECT w.id FROM sys_app_user u JOIN bc_warehouse w ON w.id = u.default_warehouse_id
             WHERE u.userid = @User AND w.use_yn AND w.warehouse_type <> 'CONSIGNMENT'),
            (SELECT id FROM bc_warehouse WHERE is_default))
        """;

    private const string SelectOverReceiptMode = "SELECT over_receipt_mode FROM mm_inventory_setting LIMIT 1";

    // Orders first, in id order, so two receipts of the same orders cannot deadlock
    private const string LockOrders = """
        SELECT p.id FROM mm_purchase_order p
        WHERE p.id IN (SELECT purchase_order_id FROM mm_purchase_order_line WHERE id = ANY(@Ids))
        ORDER BY p.id
        FOR UPDATE
        """;

    // Migrated orders may lack a status; they read as approved (MM-BR-019)
    private const string SelectPoLines = """
        SELECT l.id, l.purchase_order_id, p.po_no, l.seq AS po_seq, coalesce(p.status, 'PO') AS status, coalesce(p.vendor_id, 0) AS vendor_id,
               p.currency_code, p.exchange_rate, coalesce(l.po_qty, 0) AS po_qty, coalesce(l.receipt_qty, 0) AS received_qty
        FROM mm_purchase_order_line l JOIN mm_purchase_order p ON p.id = l.purchase_order_id
        WHERE l.id = ANY(@Ids)
        """;

    private const string AddReceiptQty = """
        UPDATE mm_purchase_order_line
        SET receipt_qty = coalesce(receipt_qty, 0) + @Qty, updated_at = now(), updated_by = @User
        WHERE id = @Id
        """;

    private const string SelectPoQuantities = """
        SELECT p.id AS purchase_order_id, p.status, coalesce(l.po_qty, 0) AS po_qty, coalesce(l.receipt_qty, 0) AS received_qty
        FROM mm_purchase_order p JOIN mm_purchase_order_line l ON l.purchase_order_id = p.id
        WHERE p.id = ANY(@Ids)
        """;

    private const string SetPoStatus = "UPDATE mm_purchase_order SET status = @Status, updated_at = now(), updated_by = @User WHERE id = @Id";

    private const string SetStandardPrice = "UPDATE bc_item SET usd_prc = @Price, updated_at = now(), updated_by = @User WHERE id = @Id";

    private const string InsertHeader = """
        INSERT INTO mm_goods_receipt (gr_no, type, vendor_id, gr_dt, warehouse_id, ref_no, remarks, currency_code, exchange_rate, supply_amt,
                                      tax_amt, tot_gr_amt, tot_base_amt, business_place_id, created_by, updated_by)
        VALUES (@GrNo, 'ITEM', @VendorId, @GrDt, @WarehouseId, @RefNo, @Remarks, @CurrencyCode, @ExchangeRate, @SupplyAmt, @TaxAmt,
                @TotGrAmt, @TotBaseAmt, @BusinessPlaceId, @User, @User)
        RETURNING id
        """;

    private const string UpdateHeader = """
        UPDATE mm_goods_receipt
        SET gr_dt = @GrDt, warehouse_id = @WarehouseId, ref_no = @RefNo, remarks = @Remarks, currency_code = @CurrencyCode,
            exchange_rate = @ExchangeRate, supply_amt = @SupplyAmt, tax_amt = @TaxAmt, tot_gr_amt = @TotGrAmt, tot_base_amt = @TotBaseAmt,
            updated_at = now(), updated_by = @User
        WHERE id = @Id AND updated_at = @UpdatedAt
        """;

    private const string DeleteLines = "DELETE FROM mm_goods_receipt_line WHERE goods_receipt_id = @Id AND id = ANY(@Ids)";

    private const string UpdateLineAmounts = """
        UPDATE mm_goods_receipt_line SET supply_amt = @SupplyAmt, tax_amt = @TaxAmt WHERE id = @LineId AND goods_receipt_id = @Id
        """;

    // Location '*' until the receipt takes a Location (TASK-CORE-073); Lot '*' until the stock posting sets the line's lot
    private const string InsertLine = $"""
        {StockKeys.EnsureStarRows}
        INSERT INTO mm_goods_receipt_line (goods_receipt_id, seq, item_id, item_name, warehouse_id, location_id, lot_id, gr_qty, gr_prc, gr_amt,
                                           tax_code_id, supply_amt, tax_amt, deductible, purchase_order_line_id, created_by, updated_by)
        VALUES (@Id, @Seq, @ItemId, @ItemName, @WarehouseId, {StockKeys.StarLocationId}, coalesce(@LotId, {StockKeys.StarLotId}), @GrQty, @GrPrc,
                @GrAmt, @TaxCodeId, @SupplyAmt, @TaxAmt, @Deductible, @PurchaseOrderLineId, @User, @User)
        """;

    private const string SetLineLot = "UPDATE mm_goods_receipt_line SET lot_id = @LotId WHERE goods_receipt_id = @Id AND seq = @Seq";

    private const string SelectOpenMovements = """
        SELECT m.id FROM mm_goods_movement m
        WHERE m.mov_type = @MovType AND m.ref_key = @GrNo AND m.ref_seq = ANY(@Seqs) AND m.parent_movement_id IS NULL
          AND NOT EXISTS (SELECT 1 FROM mm_goods_movement r WHERE r.parent_movement_id = m.id)
        ORDER BY m.id
        """;

    // Lines go with the header (ON DELETE CASCADE)
    private const string Delete = "DELETE FROM mm_goods_receipt WHERE id = @Id AND updated_at = @UpdatedAt";

    private const string SelectOpenPoLines = """
        SELECT l.id AS purchase_order_line_id, p.id AS purchase_order_id, p.po_no, l.seq AS po_seq, coalesce(p.po_dt, p.created_at::date) AS po_dt,
               coalesce(l.item_id, 0) AS item_id, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name, i.unit, l.warehouse_id,
               coalesce(l.po_qty, 0) AS po_qty, coalesce(l.receipt_qty, 0) AS received_qty,
               coalesce(l.po_qty, 0) - coalesce(l.receipt_qty, 0) AS remaining_qty, coalesce(l.po_prc, 0) AS po_prc,
               coalesce(l.dc_rate, 0) AS dc_rate, l.tax_code_id, p.currency_code, p.exchange_rate
        FROM mm_purchase_order_line l
        JOIN mm_purchase_order p ON p.id = l.purchase_order_id
        LEFT JOIN bc_item i ON i.id = l.item_id
        WHERE p.vendor_id = @VendorId AND coalesce(p.status, 'PO') IN ('PO', 'RE')
          AND coalesce(l.po_qty, 0) - coalesce(l.receipt_qty, 0) > 0
        ORDER BY p.po_no, l.seq
        """;

    public async Task<IReadOnlyList<GoodsReceiptSummaryRow>> SearchAsync(
        GoodsReceiptSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<GoodsReceiptSummaryRow>(new CommandDefinition(
            Search,
            new
            {
                GrNo = string.IsNullOrWhiteSpace(query.GrNo) ? null : query.GrNo.Trim().ToUpperInvariant(),
                query.VendorId,
                query.FromDt,
                query.ToDt,
                Limit = limit,
            },
            transaction, cancellationToken: cancellationToken))];

    public async Task<GoodsReceiptRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(Lock, new { Id = id }, transaction, cancellationToken: cancellationToken));
        }
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Find, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToGoodsReceiptRow();
    }

    public async Task<IReadOnlyList<GoodsReceiptLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<GoodsReceiptLineRow>(
            new CommandDefinition(SelectLines, new { Id = id }, transaction, cancellationToken: cancellationToken))];

    public Task<GoodsReceiptVendorRow?> FindVendorAsync(long vendorId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<GoodsReceiptVendorRow>(
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

    public Task<long?> FindDefaultWarehouseAsync(string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long?>(
            new CommandDefinition(SelectDefaultWarehouse, new { User = user }, transaction, cancellationToken: cancellationToken));

    public Task<string?> FindOverReceiptModeAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<string?>(
            new CommandDefinition(SelectOverReceiptMode, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<ReceiptPoLine>> LockPoLinesAsync(
        IReadOnlyCollection<long> purchaseOrderLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var ids = purchaseOrderLineIds.ToArray();
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(LockOrders, new { Ids = ids }, transaction, cancellationToken: cancellationToken));
        return [.. await transaction.Connection!.QueryAsync<ReceiptPoLine>(
            new CommandDefinition(SelectPoLines, new { Ids = ids }, transaction, cancellationToken: cancellationToken))];
    }

    public Task AddReceiptQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByPoLine, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            AddReceiptQty, qtyByPoLine.Select(e => new { Id = e.Key, Qty = e.Value, User = user }).ToList(), transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<(long PurchaseOrderId, string? Status, decimal PoQty, decimal ReceivedQty)>> FindPoQuantitiesAsync(
        IReadOnlyCollection<long> purchaseOrderIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<(long, string?, decimal, decimal)>(new CommandDefinition(
            SelectPoQuantities, new { Ids = purchaseOrderIds.ToArray() }, transaction, cancellationToken: cancellationToken))];

    public Task SetPoStatusAsync(long purchaseOrderId, PoStatus status, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SetPoStatus, new { Id = purchaseOrderId, Status = status.ToCode(), User = user }, transaction, cancellationToken: cancellationToken));

    public Task SetStandardPricesAsync(
        IReadOnlyDictionary<long, decimal> priceByItem, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SetStandardPrice, priceByItem.Select(e => new { Id = e.Key, Price = e.Value, User = user }).ToList(), transaction,
            cancellationToken: cancellationToken));

    public async Task<long> InsertAsync(
        string grNo, long businessPlaceId, GoodsReceiptWrite receipt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var id = await transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            InsertHeader, HeaderParameters(receipt, user, new { GrNo = grNo, BusinessPlaceId = businessPlaceId }), transaction,
            cancellationToken: cancellationToken));
        await InsertLinesAsync(id, receipt.Lines, user, transaction, cancellationToken);
        return id;
    }

    public async Task<bool> UpdateAsync(
        long id, GoodsReceiptWrite receipt, IReadOnlyCollection<long> deletedLineIds, DateTimeOffset updatedAt, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        if (await connection.ExecuteAsync(new CommandDefinition(
                UpdateHeader, HeaderParameters(receipt, user, new { Id = id, UpdatedAt = updatedAt }), transaction,
                cancellationToken: cancellationToken)) == 0)
        {
            return false;
        }
        await connection.ExecuteAsync(new CommandDefinition(
            DeleteLines, new { Id = id, Ids = deletedLineIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            UpdateLineAmounts,
            receipt.Lines.Where(l => l.Id is not null).Select(l => new { Id = id, LineId = l.Id, l.SupplyAmt, l.TaxAmt }).ToList(),
            transaction, cancellationToken: cancellationToken));
        await InsertLinesAsync(id, receipt.Lines.Where(l => l.Id is null), user, transaction, cancellationToken);
        return true;
    }

    public Task SetLineLotsAsync(long id, IReadOnlyDictionary<int, long> lotBySeq, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SetLineLot, lotBySeq.Select(e => new { Id = id, Seq = e.Key, LotId = e.Value }).ToList(), transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(
        string grNo, IReadOnlyCollection<int> seqs, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            SelectOpenMovements, new { MovType = MovementType.GoodsReceipt, GrNo = grNo, Seqs = seqs.ToArray() }, transaction,
            cancellationToken: cancellationToken))];

    public async Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Id = id, UpdatedAt = updatedAt }, transaction, cancellationToken: cancellationToken)) == 1;

    public async Task<IReadOnlyList<OpenPoLineDto>> FindOpenPoLinesAsync(long vendorId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<OpenPoLineDto>(
            new CommandDefinition(SelectOpenPoLines, new { VendorId = vendorId }, transaction, cancellationToken: cancellationToken))];

    private static async Task InsertLinesAsync(
        long id, IEnumerable<GoodsReceiptLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var l in lines)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                InsertLine,
                new
                {
                    Id = id,
                    l.Seq,
                    l.ItemId,
                    l.ItemName,
                    l.WarehouseId,
                    l.LotId,
                    l.GrQty,
                    l.GrPrc,
                    l.GrAmt,
                    l.TaxCodeId,
                    l.SupplyAmt,
                    l.TaxAmt,
                    l.Deductible,
                    l.PurchaseOrderLineId,
                    User = user,
                },
                transaction, cancellationToken: cancellationToken));
        }
    }

    private static DynamicParameters HeaderParameters(GoodsReceiptWrite receipt, string user, object keys)
    {
        var h = receipt.Header;
        var t = receipt.Totals;
        var parameters = new DynamicParameters(new
        {
            h.VendorId,
            h.GrDt,
            h.WarehouseId,
            h.RefNo,
            h.Remarks,
            h.CurrencyCode,
            h.ExchangeRate,
            t.SupplyAmt,
            t.TaxAmt,
            t.TotGrAmt,
            t.TotBaseAmt,
            User = user,
        });
        parameters.AddDynamicParams(keys);
        return parameters;
    }

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id, string GrNo, long VendorId, string? VendorName, DateOnly GrDt, long WarehouseId, string? RefNo, string? Remarks,
        string CurrencyCode, decimal ExchangeRate, decimal SupplyAmt, decimal TaxAmt, decimal TotGrAmt, decimal TotBaseAmt,
        DateTime UpdatedAt, DateTime? CreatedAt, string? CreatedBy, string? UpdatedBy)
    {
        public GoodsReceiptRow ToGoodsReceiptRow() => new(
            Id, GrNo, VendorId, VendorName, GrDt, WarehouseId, RefNo, Remarks, CurrencyCode, ExchangeRate, SupplyAmt, TaxAmt, TotGrAmt,
            TotBaseAmt, Utc(UpdatedAt), Utc(CreatedAt), CreatedBy, UpdatedBy);

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

        private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;
    }
}

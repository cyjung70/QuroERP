using System.Data;
using Dapper;
using QuroErp.Application.Sd.Shipments;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Domain.Sd.Shipments;

namespace QuroErp.Infrastructure.Sd.Shipments;

internal sealed class ShipmentRepository : IShipmentRepository
{
    private const string HeaderColumns = """
        s.id, s.ship_no, s.status, s.billing_flow, s.customer_id, c.c_name AS customer_name, s.sales_order_id, o.so_no, s.ship_dt,
        s.warehouse_id, s.currency_code, s.exchange_rate, s.ship_via, s.ship_name, s.ship_addr1, s.ship_addr2, s.ship_city, s.ship_state,
        s.ship_zip_code, s.ship_country, s.ship_phone, s.remarks, s.sales_invoice_id, d.dn_no AS sales_invoice_no, s.updated_at, s.created_at,
        s.created_by, s.updated_by
        """;

    private const string HeaderFrom = """
        FROM sd_shipment s
        JOIN bc_customer c ON c.id = s.customer_id
        LEFT JOIN sd_sales_order o ON o.id = s.sales_order_id
        LEFT JOIN sd_sales_invoice d ON d.id = s.sales_invoice_id
        """;

    private const string Search = $"""
        SELECT {HeaderColumns}
        {HeaderFrom}
        WHERE (@ShipNo::varchar IS NULL OR starts_with(s.ship_no, @ShipNo))
          AND (@CustomerId::bigint IS NULL OR s.customer_id = @CustomerId)
          AND (@CustomerNamePattern::text IS NULL OR c.c_name ILIKE @CustomerNamePattern ESCAPE '\')
          AND (@Status::varchar IS NULL OR s.status = @Status)
          AND (@FromDt::date IS NULL OR s.ship_dt >= @FromDt)
          AND (@ToDt::date IS NULL OR s.ship_dt <= @ToDt)
        ORDER BY s.ship_dt DESC, s.ship_no DESC
        LIMIT @Limit
        """;

    private const string SelectAmountLines = """
        SELECT shipment_id, seq, ship_qty, ship_prc, coalesce(dc_rate, 0) AS dc_rate, tax_code_id
        FROM sd_shipment_line
        WHERE shipment_id = ANY(@Ids)
        ORDER BY shipment_id, seq
        """;

    private const string Find = $"""
        SELECT {HeaderColumns}
        {HeaderFrom}
        WHERE s.id = @Id
        """;

    private const string Lock = "SELECT id FROM sd_shipment WHERE id = @Id FOR UPDATE";

    private const string SelectLines = """
        SELECT l.id, l.seq, l.item_id, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name, i.unit, l.warehouse_id, w.warehouse_code,
               l.lot_id, t.lot_no, l.ship_qty, l.ship_prc, l.dc_cd, coalesce(l.dc_rate, 0) AS dc_rate, l.tax_code_id, l.billed_qty,
               coalesce((SELECT h.onhand_qty FROM mm_onhand h WHERE h.item_id = l.item_id AND h.warehouse_id = l.warehouse_id), 0) AS onhand_qty,
               l.sales_order_line_id, o.so_no, ol.seq AS so_seq, l.sales_invoice_line_id, d.dn_no, dl.seq AS dn_seq,
               CASE WHEN l.sales_invoice_line_id IS NOT NULL THEN coalesce(dl.dn_qty, 0) - dl.shipped_qty - dl.cancelled_qty
                    WHEN l.sales_order_line_id IS NOT NULL THEN coalesce(ol.so_qty, 0) - ol.ship_qty END AS remaining_qty
        FROM sd_shipment_line l
        LEFT JOIN bc_item i ON i.id = l.item_id
        LEFT JOIN bc_warehouse w ON w.id = l.warehouse_id
        LEFT JOIN mm_lot t ON t.id = l.lot_id
        LEFT JOIN sd_sales_order_line ol ON ol.id = l.sales_order_line_id
        LEFT JOIN sd_sales_order o ON o.id = ol.sales_order_id
        LEFT JOIN sd_sales_invoice_line dl ON dl.id = l.sales_invoice_line_id
        LEFT JOIN sd_sales_invoice d ON d.id = dl.sales_invoice_id
        WHERE l.shipment_id = @Id
        ORDER BY l.seq
        """;

    // Orders in id order, so two shipments of the same orders cannot deadlock
    private const string LockOrders = """
        SELECT id FROM sd_sales_order
        WHERE id IN (SELECT sales_order_id FROM sd_sales_order_line WHERE id = ANY(@Ids))
        ORDER BY id
        FOR UPDATE
        """;

    private const string SelectOrderLines = """
        SELECT l.id, l.sales_order_id, o.so_no, l.seq, coalesce(o.customer_id, 0) AS customer_id, o.billing_flow, o.currency_code, o.exchange_rate,
               coalesce(l.item_id, 0) AS item_id, l.item_name, coalesce(l.so_qty, 0) AS so_qty, l.ship_qty, coalesce(l.so_prc, 0) AS so_prc,
               l.dc_cd, coalesce(l.dc_rate, 0) AS dc_rate, l.tax_code_id
        FROM sd_sales_order_line l JOIN sd_sales_order o ON o.id = l.sales_order_id
        WHERE l.id = ANY(@Ids)
        """;

    private const string LockInvoices = """
        SELECT id FROM sd_sales_invoice
        WHERE id IN (SELECT sales_invoice_id FROM sd_sales_invoice_line WHERE id = ANY(@Ids))
        ORDER BY id
        FOR UPDATE
        """;

    private const string SelectInvoiceLines = """
        SELECT l.id, l.sales_invoice_id, d.dn_no, l.seq, coalesce(d.customer_id, 0) AS customer_id, coalesce(d.status, 'OP') AS status,
               d.billing_flow, d.currency_code, d.exchange_rate, coalesce(l.item_id, 0) AS item_id, l.item_name, coalesce(l.dn_qty, 0) AS dn_qty,
               l.shipped_qty, l.cancelled_qty, coalesce(l.dn_prc, 0) AS dn_prc, l.dc_cd, coalesce(l.dc_rate, 0) AS dc_rate, l.tax_code_id,
               l.sales_order_line_id
        FROM sd_sales_invoice_line l JOIN sd_sales_invoice d ON d.id = l.sales_invoice_id
        WHERE l.id = ANY(@Ids)
        """;

    private const string SelectOrderTerms = "SELECT rep, terms, due_dt, freight_charge FROM sd_sales_order WHERE id = @Id";

    private const string SelectItemCodes = "SELECT id, item_cd FROM bc_item WHERE id = ANY(@Ids)";

    private const string SelectUsableWarehouses = "SELECT id FROM bc_warehouse WHERE id = ANY(@Ids) AND use_yn";

    private const string SelectDefaultWarehouse = """
        SELECT coalesce(
            (SELECT w.id FROM sys_app_user u JOIN bc_warehouse w ON w.id = u.default_warehouse_id
             WHERE u.userid = @User AND w.use_yn AND w.warehouse_type <> 'CONSIGNMENT'),
            (SELECT id FROM bc_warehouse WHERE is_default))
        """;

    private const string SelectOpenMovements = """
        SELECT m.id FROM mm_goods_movement m
        WHERE m.mov_type = 'DN' AND m.ref_key = ANY(@RefKeys) AND m.ref_seq = ANY(@Seqs) AND m.parent_movement_id IS NULL
          AND NOT EXISTS (SELECT 1 FROM mm_goods_movement r WHERE r.parent_movement_id = m.id)
        ORDER BY m.id
        """;

    private const string InsertHeader = """
        INSERT INTO sd_shipment (ship_no, customer_id, sales_order_id, ship_dt, warehouse_id, currency_code, exchange_rate, status, billing_flow,
                                 ship_via, ship_name, ship_addr1, ship_addr2, ship_city, ship_state, ship_zip_code, ship_country, ship_phone,
                                 remarks, business_place_id, created_by, updated_by)
        VALUES (@ShipNo, @CustomerId, @SalesOrderId, @ShipDt, @WarehouseId, @CurrencyCode, @ExchangeRate, @Status, @BillingFlow, @ShipVia,
                @ShipName, @ShipAddr1, @ShipAddr2, @ShipCity, @ShipState, @ShipZipCode, @ShipCountry, @ShipPhone, @Remarks, @BusinessPlaceId,
                @User, @User)
        RETURNING id
        """;

    private const string UpdateHeader = """
        UPDATE sd_shipment
        SET sales_order_id = @SalesOrderId, warehouse_id = @WarehouseId, ship_via = @ShipVia, ship_name = @ShipName, ship_addr1 = @ShipAddr1,
            ship_addr2 = @ShipAddr2, ship_city = @ShipCity, ship_state = @ShipState, ship_zip_code = @ShipZipCode, ship_country = @ShipCountry,
            ship_phone = @ShipPhone, remarks = @Remarks, updated_at = now(), updated_by = @User
        WHERE id = @Id AND updated_at = @UpdatedAt
        """;

    // SD-BR-167: the SHIP_AND_BILL invoice lines of the deleted lines go first (they reference them)
    private const string DeleteInvoiceLinesOf = "DELETE FROM sd_sales_invoice_line WHERE shipment_line_id = ANY(@Ids)";

    private const string DeleteLines = "DELETE FROM sd_shipment_line WHERE shipment_id = @Id AND id = ANY(@Ids)";

    private const string InsertLine = """
        INSERT INTO sd_shipment_line (shipment_id, seq, item_id, item_name, warehouse_id, ship_qty, ship_prc, dc_cd, dc_rate, billed_qty,
                                      sales_order_line_id, tax_code_id, lot_id, sales_invoice_line_id, created_by, updated_by)
        VALUES (@Id, @Seq, @ItemId, @ItemName, @WarehouseId, @ShipQty, @ShipPrc, @DcCd, @DcRate, @BilledQty, @SalesOrderLineId, @TaxCodeId,
                @LotId, @SalesInvoiceLineId, @User, @User)
        RETURNING id
        """;

    private const string SetLineLot = "UPDATE sd_shipment_line SET lot_id = @LotId WHERE shipment_id = @Id AND seq = @Seq";

    private const string SetStatus = """
        UPDATE sd_shipment SET status = @Status, sales_invoice_id = @SalesInvoiceId, updated_at = now(), updated_by = @User WHERE id = @Id
        """;

    private const string VoidHeader = """
        UPDATE sd_shipment SET status = 'VO', updated_at = now(), updated_by = @User WHERE id = @Id AND updated_at = @UpdatedAt
        """;

    private const string VoidLines = """
        UPDATE sd_shipment_line SET billed_qty = 0, updated_at = now(), updated_by = @User WHERE shipment_id = @Id
        """;

    private const string AddOrderShipQty = """
        UPDATE sd_sales_order_line SET ship_qty = ship_qty + @Qty, updated_at = now(), updated_by = @User WHERE id = @Id
        """;

    private const string AddOrderDnQty = """
        UPDATE sd_sales_order_line SET dn_qty = coalesce(dn_qty, 0) + @Qty, updated_at = now(), updated_by = @User WHERE id = @Id
        """;

    private const string AddInvoiceShippedQty = """
        UPDATE sd_sales_invoice_line SET shipped_qty = shipped_qty + @Qty, updated_at = now(), updated_by = @User WHERE id = @Id
        """;

    private const string SelectOrderQuantities = """
        SELECT l.sales_order_id, coalesce(o.status, 'SO') AS status, coalesce(l.so_qty, 0) AS so_qty, l.ship_qty
        FROM sd_sales_order_line l JOIN sd_sales_order o ON o.id = l.sales_order_id
        WHERE l.sales_order_id = ANY(@Ids)
        """;

    private const string SetOrderStatus = "UPDATE sd_sales_order SET status = @Status, updated_at = now(), updated_by = @User WHERE id = @Id";

    private const string LockInvoice = "SELECT id FROM sd_sales_invoice WHERE id = @Id FOR UPDATE";

    private const string SelectInvoice = """
        SELECT id, dn_no, coalesce(tot_dn_amt, 0) AS tot_dn_amt, coalesce(bal_amt, 0) AS bal_amt, status FROM sd_sales_invoice WHERE id = @Id
        """;

    private const string InsertInvoice = """
        INSERT INTO sd_sales_invoice (dn_no, customer_id, sales_order_id, dn_dt, ship_dt, due_dt, tot_dn_amt, bal_amt, remarks, terms, type, rep,
                                      ship_via, freight_charge, ship_name, ship_addr1, ship_addr2, ship_city, ship_state, ship_zip_code,
                                      ship_country, ship_phone, status, currency_code, exchange_rate, supply_amt, tax_amt, tot_base_amt,
                                      warehouse_id, business_place_id, billing_flow, created_by, updated_by)
        VALUES (@DnNo, @CustomerId, @SalesOrderId, @DnDt, @DnDt, @DueDt, 0, 0, @Remarks, @Terms, @Type, @Rep, @ShipVia, @FreightCharge,
                @ShipName, @ShipAddr1, @ShipAddr2, @ShipCity, @ShipState, @ShipZipCode, @ShipCountry, @ShipPhone, 'OP', @CurrencyCode,
                @ExchangeRate, 0, 0, 0, @WarehouseId, @BusinessPlaceId, 'SHIP_AND_BILL', @User, @User)
        RETURNING id
        """;

    private const string InsertInvoiceLine = """
        INSERT INTO sd_sales_invoice_line (sales_invoice_id, seq, item_id, item_name, dn_qty, dn_prc, dn_amt, sales_order_line_id, dc_cd, dc_rate,
                                           warehouse_id, tax_code_id, supply_amt, tax_amt, shipment_line_id, created_by, updated_by)
        VALUES (@Id, @Seq, @ItemId, @ItemName, @DnQty, @DnPrc, @DnAmt, @SalesOrderLineId, @DcCd, @DcRate, @WarehouseId, @TaxCodeId, @SupplyAmt,
                @TaxAmt, @ShipmentLineId, @User, @User)
        """;

    private const string SelectInvoiceLineInputs = """
        SELECT seq, coalesce(dn_qty, 0) AS qty, coalesce(dn_prc, 0) AS prc, coalesce(dc_rate, 0) AS dc_rate, tax_code_id
        FROM sd_sales_invoice_line
        WHERE sales_invoice_id = @Id
        ORDER BY seq
        """;

    private const string UpdateInvoiceLineAmounts = """
        UPDATE sd_sales_invoice_line SET dn_amt = @DnAmt, supply_amt = @SupplyAmt, tax_amt = @TaxAmt, updated_at = now(), updated_by = @User
        WHERE sales_invoice_id = @Id AND seq = @Seq
        """;

    private const string UpdateInvoiceHeader = """
        UPDATE sd_sales_invoice
        SET due_dt = @DueDt, terms = @Terms, rep = @Rep, ship_via = @ShipVia, freight_charge = @FreightCharge, ship_name = @ShipName,
            ship_addr1 = @ShipAddr1, ship_addr2 = @ShipAddr2, ship_city = @ShipCity, ship_state = @ShipState, ship_zip_code = @ShipZipCode,
            ship_country = @ShipCountry, ship_phone = @ShipPhone, remarks = @Remarks, warehouse_id = @WarehouseId, sales_order_id = @SalesOrderId,
            supply_amt = @SupplyAmt, tax_amt = @TaxAmt, tot_dn_amt = @TotDnAmt, bal_amt = @TotDnAmt, tot_base_amt = @TotBaseAmt, status = 'OP',
            updated_at = now(), updated_by = @User
        WHERE id = @Id
        """;

    private const string VoidInvoiceLines = """
        UPDATE sd_sales_invoice_line SET dn_qty = 0, dn_amt = 0, supply_amt = 0, tax_amt = 0, updated_at = now(), updated_by = @User
        WHERE sales_invoice_id = @Id
        """;

    private const string VoidInvoiceHeader = """
        UPDATE sd_sales_invoice
        SET status = 'VO', supply_amt = 0, tax_amt = 0, tot_dn_amt = 0, bal_amt = 0, tot_base_amt = 0, updated_at = now(), updated_by = @User
        WHERE id = @Id
        """;

    private const string SelectOpenOrderLines = """
        SELECT o.id AS sales_order_id, o.so_no, coalesce(o.so_dt, o.created_at::date) AS so_dt, o.currency_code, o.exchange_rate, o.billing_flow,
               o.rep, o.ship_via, o.ship_name, o.ship_addr1, o.ship_addr2, o.ship_city, o.ship_state, o.ship_zip_code, o.ship_country, o.ship_phone,
               l.id AS sales_order_line_id, l.seq, l.item_id, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name, i.unit,
               coalesce(l.so_qty, 0) AS so_qty, l.ship_qty, coalesce(l.so_prc, 0) AS so_prc, l.dc_cd, coalesce(l.dc_rate, 0) AS dc_rate,
               l.tax_code_id
        FROM sd_sales_order o
        JOIN sd_sales_order_line l ON l.sales_order_id = o.id
        JOIN bc_item i ON i.id = l.item_id
        WHERE o.customer_id = @CustomerId AND coalesce(o.status, 'SO') <> 'CL' AND o.billing_flow <> 'BILL_THEN_SHIP'
          AND coalesce(l.so_qty, 0) - l.ship_qty > 0
        ORDER BY o.so_dt NULLS LAST, o.so_no, l.seq
        """;

    private const string SelectUnbilledLines = """
        SELECT s.id AS shipment_id, s.ship_no, s.ship_dt, l.id AS shipment_line_id, l.seq, l.item_id, i.item_cd,
               coalesce(l.item_name, i.item_name) AS item_name, l.warehouse_id, l.ship_qty, l.billed_qty, l.ship_qty - l.billed_qty AS unbilled_qty,
               l.ship_prc, l.dc_cd, coalesce(l.dc_rate, 0) AS dc_rate, l.tax_code_id, ol.sales_order_id, o.so_no
        FROM sd_shipment s
        JOIN sd_shipment_line l ON l.shipment_id = s.id
        LEFT JOIN bc_item i ON i.id = l.item_id
        LEFT JOIN sd_sales_order_line ol ON ol.id = l.sales_order_line_id
        LEFT JOIN sd_sales_order o ON o.id = ol.sales_order_id
        WHERE s.customer_id = @CustomerId AND s.currency_code = @CurrencyCode AND s.billing_flow = 'SHIP_THEN_BILL' AND s.status <> 'VO'
          AND l.ship_qty - l.billed_qty > 0
        ORDER BY s.ship_dt, s.ship_no, l.seq
        """;

    private const string SelectUnshippedInvoiceLines = """
        SELECT l.id AS sales_invoice_line_id, d.id AS sales_invoice_id, d.dn_no, l.seq, l.item_id, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name,
               coalesce(l.dn_qty, 0) AS invoiced_qty, l.shipped_qty, l.cancelled_qty,
               coalesce(l.dn_qty, 0) - l.shipped_qty - l.cancelled_qty AS unshipped_qty, coalesce(l.dn_prc, 0) AS prc, l.dc_cd,
               coalesce(l.dc_rate, 0) AS dc_rate, l.tax_code_id, d.currency_code, d.exchange_rate, i.unit
        FROM sd_sales_invoice d
        JOIN sd_sales_invoice_line l ON l.sales_invoice_id = d.id
        JOIN bc_item i ON i.id = l.item_id
        WHERE d.customer_id = @CustomerId AND d.billing_flow = 'BILL_THEN_SHIP' AND coalesce(d.status, 'OP') <> 'VO'
          AND coalesce(l.dn_qty, 0) - l.shipped_qty - l.cancelled_qty > 0
        ORDER BY d.dn_dt NULLS LAST, d.dn_no, l.seq
        """;

    private const string SelectPrintLines = """
        SELECT l.seq, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name, i.unit, w.warehouse_code, t.lot_no, l.ship_qty
        FROM sd_shipment_line l
        LEFT JOIN bc_item i ON i.id = l.item_id
        LEFT JOIN bc_warehouse w ON w.id = l.warehouse_id
        LEFT JOIN mm_lot t ON t.id = l.lot_id
        WHERE l.shipment_id = @Id
        ORDER BY l.seq
        """;

    private const string InvoiceType = "IV";

    public async Task<IReadOnlyList<ShipmentRow>> SearchAsync(
        ShipmentSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. (await transaction.Connection!.QueryAsync<Row>(new CommandDefinition(
            Search,
            new
            {
                ShipNo = string.IsNullOrWhiteSpace(query.ShipNo) ? null : query.ShipNo.Trim().ToUpperInvariant(),
                query.CustomerId,
                CustomerNamePattern = string.IsNullOrWhiteSpace(query.CustomerName) ? null : "%" + EscapeLike(query.CustomerName.Trim()) + "%",
                Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status,
                query.FromDt,
                query.ToDt,
                Limit = limit,
            },
            transaction, cancellationToken: cancellationToken))).Select(r => r.ToShipmentRow())];

    public async Task<IReadOnlyList<ShipmentAmountLineRow>> FindAmountLinesAsync(
        IReadOnlyList<long> shipmentIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<ShipmentAmountLineRow>(new CommandDefinition(
            SelectAmountLines, new { Ids = shipmentIds.ToArray() }, transaction, cancellationToken: cancellationToken))).AsList();

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    public async Task<ShipmentRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(Lock, new { Id = id }, transaction, cancellationToken: cancellationToken));
        }
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Find, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToShipmentRow();
    }

    public async Task<IReadOnlyList<ShipmentLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<ShipmentLineRow>(
            new CommandDefinition(SelectLines, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<ShipmentOrderLine>> LockOrderLinesAsync(
        IReadOnlyCollection<long> salesOrderLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new { Ids = salesOrderLineIds.ToArray() };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(LockOrders, parameters, transaction, cancellationToken: cancellationToken));
        return (await transaction.Connection!.QueryAsync<ShipmentOrderLine>(
            new CommandDefinition(SelectOrderLines, parameters, transaction, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<ShipmentInvoiceLine>> LockInvoiceLinesAsync(
        IReadOnlyCollection<long> salesInvoiceLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new { Ids = salesInvoiceLineIds.ToArray() };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(LockInvoices, parameters, transaction, cancellationToken: cancellationToken));
        return (await transaction.Connection!.QueryAsync<ShipmentInvoiceLine>(
            new CommandDefinition(SelectInvoiceLines, parameters, transaction, cancellationToken: cancellationToken))).AsList();
    }

    public Task<ShipmentOrderTerms?> FindOrderTermsAsync(long salesOrderId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<ShipmentOrderTerms?>(
            new CommandDefinition(SelectOrderTerms, new { Id = salesOrderId }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyDictionary<long, string>> FindItemCodesAsync(
        IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string ItemCd)>(new CommandDefinition(
            SelectItemCodes, new { Ids = itemIds.ToArray() }, transaction, cancellationToken: cancellationToken)))
        .ToDictionary(r => r.Id, r => r.ItemCd);

    public async Task<IReadOnlySet<long>> FindUsableWarehousesAsync(
        IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            SelectUsableWarehouses, new { Ids = warehouseIds.ToArray() }, transaction, cancellationToken: cancellationToken))).ToHashSet();

    public Task<long?> FindDefaultWarehouseAsync(string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(SelectDefaultWarehouse, new { User = user }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(
        IReadOnlyCollection<string> refKeys, IReadOnlyCollection<int> seqs, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            SelectOpenMovements, new { RefKeys = refKeys.ToArray(), Seqs = seqs.ToArray() }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<(long Id, IReadOnlyDictionary<int, long> LineIds)> InsertAsync(
        string shipNo, long businessPlaceId, ShipmentHeader header, string status, IReadOnlyList<ShipmentLine> lines, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var id = await transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            InsertHeader, HeaderParameters(header, user, new { ShipNo = shipNo, BusinessPlaceId = businessPlaceId, Status = status }), transaction,
            cancellationToken: cancellationToken));
        return (id, await InsertLinesAsync(id, lines, user, transaction, cancellationToken));
    }

    public async Task<IReadOnlyDictionary<int, long>?> UpdateAsync(
        long id, ShipmentHeader header, IReadOnlyCollection<long> deletedLineIds, IReadOnlyList<ShipmentLine> added, DateTimeOffset updatedAt,
        string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        if (await connection.ExecuteAsync(new CommandDefinition(
                UpdateHeader, HeaderParameters(header, user, new { Id = id, UpdatedAt = updatedAt }), transaction,
                cancellationToken: cancellationToken)) == 0)
        {
            return null;
        }
        if (deletedLineIds.Count > 0)
        {
            var ids = new { Id = id, Ids = deletedLineIds.ToArray() };
            await connection.ExecuteAsync(new CommandDefinition(DeleteInvoiceLinesOf, ids, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(DeleteLines, ids, transaction, cancellationToken: cancellationToken));
        }
        return await InsertLinesAsync(id, added, user, transaction, cancellationToken);
    }

    public async Task SetLineLotsAsync(long id, IReadOnlyDictionary<int, long> lotIdBySeq, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var (seq, lotId) in lotIdBySeq)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                SetLineLot, new { Id = id, Seq = seq, LotId = lotId }, transaction, cancellationToken: cancellationToken));
        }
    }

    public Task SetStatusAsync(long id, string status, long? salesInvoiceId, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SetStatus, new { Id = id, Status = status, SalesInvoiceId = salesInvoiceId, User = user }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> VoidAsync(long id, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        if (await connection.ExecuteAsync(new CommandDefinition(
                VoidHeader, new { Id = id, UpdatedAt = updatedAt, User = user }, transaction, cancellationToken: cancellationToken)) == 0)
        {
            return false;
        }
        await connection.ExecuteAsync(new CommandDefinition(VoidLines, new { Id = id, User = user }, transaction, cancellationToken: cancellationToken));
        return true;
    }

    public Task AddOrderShipQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByOrderLine, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        AddQtyAsync(AddOrderShipQty, qtyByOrderLine, user, transaction, cancellationToken);

    public Task AddOrderDnQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByOrderLine, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        AddQtyAsync(AddOrderDnQty, qtyByOrderLine, user, transaction, cancellationToken);

    public Task AddInvoiceShippedQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByInvoiceLine, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        AddQtyAsync(AddInvoiceShippedQty, qtyByInvoiceLine, user, transaction, cancellationToken);

    public async Task<IReadOnlyList<ShipmentOrderQuantity>> FindOrderQuantitiesAsync(
        IReadOnlyCollection<long> salesOrderIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<ShipmentOrderQuantity>(new CommandDefinition(
            SelectOrderQuantities, new { Ids = salesOrderIds.ToArray() }, transaction, cancellationToken: cancellationToken))).AsList();

    public Task SetOrderStatusAsync(long salesOrderId, string status, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SetOrderStatus, new { Id = salesOrderId, Status = status, User = user }, transaction, cancellationToken: cancellationToken));

    public async Task<ShipmentInvoiceRow?> FindInvoiceAsync(
        long salesInvoiceId, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            await transaction.Connection!.ExecuteAsync(
                new CommandDefinition(LockInvoice, new { Id = salesInvoiceId }, transaction, cancellationToken: cancellationToken));
        }
        return await transaction.Connection!.QuerySingleOrDefaultAsync<ShipmentInvoiceRow>(
            new CommandDefinition(SelectInvoice, new { Id = salesInvoiceId }, transaction, cancellationToken: cancellationToken));
    }

    public Task<long> InsertInvoiceAsync(
        string dnNo, long businessPlaceId, ShipmentInvoiceHeader header, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            InsertInvoice, InvoiceParameters(header, user, new { DnNo = dnNo, BusinessPlaceId = businessPlaceId, Type = InvoiceType }), transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<ShipmentInvoiceInput>> FindInvoiceLineInputsAsync(
        long salesInvoiceId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<ShipmentInvoiceInput>(new CommandDefinition(
            SelectInvoiceLineInputs, new { Id = salesInvoiceId }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task UpdateInvoiceAsync(
        long salesInvoiceId, ShipmentInvoiceHeader header, IReadOnlyList<ShipmentInvoiceLineWrite> added,
        IReadOnlyList<ShipmentInvoiceLineAmounts> amounts, ShipmentInvoiceTotals totals, string user, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        foreach (var l in added)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertInvoiceLine,
                new
                {
                    Id = salesInvoiceId,
                    l.Seq,
                    l.ItemId,
                    l.ItemName,
                    l.DnQty,
                    l.DnPrc,
                    l.DnAmt,
                    l.SalesOrderLineId,
                    l.DcCd,
                    l.DcRate,
                    l.WarehouseId,
                    l.TaxCodeId,
                    l.SupplyAmt,
                    l.TaxAmt,
                    l.ShipmentLineId,
                    User = user,
                },
                transaction, cancellationToken: cancellationToken));
        }
        foreach (var a in amounts)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                UpdateInvoiceLineAmounts, new { Id = salesInvoiceId, a.Seq, a.DnAmt, a.SupplyAmt, a.TaxAmt, User = user }, transaction,
                cancellationToken: cancellationToken));
        }
        await connection.ExecuteAsync(new CommandDefinition(
            UpdateInvoiceHeader,
            InvoiceParameters(header, user, new { Id = salesInvoiceId, totals.SupplyAmt, totals.TaxAmt, totals.TotDnAmt, totals.TotBaseAmt }),
            transaction, cancellationToken: cancellationToken));
    }

    public async Task VoidInvoiceAsync(long salesInvoiceId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new { Id = salesInvoiceId, User = user };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(VoidInvoiceLines, parameters, transaction, cancellationToken: cancellationToken));
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(VoidInvoiceHeader, parameters, transaction, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<OpenOrderLineRow>> FindOpenOrderLinesAsync(long customerId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<OpenOrderLineRow>(new CommandDefinition(
            SelectOpenOrderLines, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<UnbilledShipmentLineDto>> FindUnbilledLinesAsync(
        long customerId, string currencyCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<UnbilledShipmentLineDto>(new CommandDefinition(
            SelectUnbilledLines, new { CustomerId = customerId, CurrencyCode = currencyCode }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<UnshippedInvoiceLineDto>> FindUnshippedInvoiceLinesAsync(
        long customerId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<UnshippedInvoiceLineDto>(new CommandDefinition(
            SelectUnshippedInvoiceLines, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<ShipmentPrintLine>> FindPrintLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<ShipmentPrintLine>(
            new CommandDefinition(SelectPrintLines, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    private static async Task<IReadOnlyDictionary<int, long>> InsertLinesAsync(
        long id, IReadOnlyList<ShipmentLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var ids = new Dictionary<int, long>(lines.Count);
        foreach (var l in lines)
        {
            ids[l.Seq] = await transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
                InsertLine,
                new
                {
                    Id = id,
                    l.Seq,
                    l.ItemId,
                    l.ItemName,
                    l.WarehouseId,
                    l.ShipQty,
                    l.ShipPrc,
                    l.DcCd,
                    l.DcRate,
                    l.BilledQty,
                    l.SalesOrderLineId,
                    l.TaxCodeId,
                    l.LotId,
                    l.SalesInvoiceLineId,
                    User = user,
                },
                transaction, cancellationToken: cancellationToken));
        }
        return ids;
    }

    private static async Task AddQtyAsync(
        string sql, IReadOnlyDictionary<long, decimal> qtyById, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        // Rows in id order, as the other documents touching them
        foreach (var (id, qty) in qtyById.OrderBy(q => q.Key))
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                sql, new { Id = id, Qty = qty, User = user }, transaction, cancellationToken: cancellationToken));
        }
    }

    private static DynamicParameters HeaderParameters(ShipmentHeader h, string user, object keys)
    {
        var s = h.ShipTo;
        var parameters = new DynamicParameters(new
        {
            h.CustomerId,
            h.SalesOrderId,
            h.ShipDt,
            h.WarehouseId,
            h.CurrencyCode,
            h.ExchangeRate,
            h.BillingFlow,
            h.ShipVia,
            s.ShipName,
            s.ShipAddr1,
            s.ShipAddr2,
            s.ShipCity,
            s.ShipState,
            s.ShipZipCode,
            s.ShipCountry,
            s.ShipPhone,
            h.Remarks,
            User = user,
        });
        parameters.AddDynamicParams(keys);
        return parameters;
    }

    private static DynamicParameters InvoiceParameters(ShipmentInvoiceHeader h, string user, object keys)
    {
        var s = h.ShipTo;
        var parameters = new DynamicParameters(new
        {
            h.CustomerId,
            h.SalesOrderId,
            h.DnDt,
            h.DueDt,
            h.Terms,
            h.Rep,
            h.ShipVia,
            h.FreightCharge,
            s.ShipName,
            s.ShipAddr1,
            s.ShipAddr2,
            s.ShipCity,
            s.ShipState,
            s.ShipZipCode,
            s.ShipCountry,
            s.ShipPhone,
            h.CurrencyCode,
            h.ExchangeRate,
            h.WarehouseId,
            h.Remarks,
            User = user,
        });
        parameters.AddDynamicParams(keys);
        return parameters;
    }

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id, string ShipNo, string Status, string BillingFlow, long CustomerId, string? CustomerName, long? SalesOrderId, string? SoNo,
        DateOnly ShipDt, long WarehouseId, string CurrencyCode, decimal ExchangeRate, string? ShipVia, string? ShipName, string? ShipAddr1,
        string? ShipAddr2, string? ShipCity, string? ShipState, string? ShipZipCode, string? ShipCountry, string? ShipPhone, string? Remarks,
        long? SalesInvoiceId, string? SalesInvoiceNo, DateTime UpdatedAt, DateTime? CreatedAt, string? CreatedBy, string? UpdatedBy)
    {
        public ShipmentRow ToShipmentRow() => new(
            Id, ShipNo, Status, BillingFlow, CustomerId, CustomerName, SalesOrderId, SoNo, ShipDt, WarehouseId, CurrencyCode, ExchangeRate, ShipVia,
            new ShipmentShipTo(ShipName, ShipAddr1, ShipAddr2, ShipCity, ShipState, ShipZipCode, ShipCountry, ShipPhone), Remarks, SalesInvoiceId,
            SalesInvoiceNo, Utc(UpdatedAt), CreatedAt is { } c ? Utc(c) : null, CreatedBy, UpdatedBy);

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

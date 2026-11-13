using System.Data;
using Dapper;
using QuroErp.Application.Sd.SalesInvoices;
using QuroErp.Contracts.Sd.SalesInvoices;
using QuroErp.Domain.Sd.SalesInvoices;
using QuroErp.Domain.Sd.SalesOrders;
using QuroErp.Domain.Sd.Shipments;

using QuroErp.Infrastructure.Mm.Inventory;

namespace QuroErp.Infrastructure.Sd.SalesInvoices;

internal sealed class SalesInvoiceRepository : ISalesInvoiceRepository
{
    private const string HeaderColumns = """
        d.id, d.dn_no, coalesce(d.status, 'OP') AS status, d.billing_flow, coalesce(d.customer_id, 0) AS customer_id, c.c_name AS customer_name,
        d.sales_order_id, o.so_no, coalesce(d.dn_dt, d.created_at::date) AS dn_dt, d.due_dt, d.warehouse_id, d.business_place_id, d.currency_code,
        d.exchange_rate, d.rep, d.terms, d.ship_via, d.freight_charge, d.ship_name, d.ship_addr1, d.ship_addr2, d.ship_city, d.ship_state,
        d.ship_zip_code, d.ship_country, d.ship_phone, d.ship_fax, d.remarks, d.tax_exemption_cert_no, d.supply_amt, d.tax_amt,
        coalesce(d.tot_dn_amt, 0) AS tot_dn_amt, d.tot_base_amt, coalesce(d.bal_amt, 0) AS bal_amt, s.id AS shipment_id, s.ship_no,
        d.updated_at, d.created_at, d.created_by, d.updated_by
        """;

    // SD-BR-120: the invoice API reads type IV only
    private const string HeaderFrom = """
        FROM sd_sales_invoice d
        LEFT JOIN bc_customer c ON c.id = d.customer_id
        LEFT JOIN sd_sales_order o ON o.id = d.sales_order_id
        LEFT JOIN LATERAL (SELECT x.id, x.ship_no FROM sd_shipment x WHERE x.sales_invoice_id = d.id ORDER BY x.id LIMIT 1) s ON true
        """;

    private const string Search = $"""
        SELECT {HeaderColumns}
        {HeaderFrom}
        WHERE d.type = 'IV'
          AND (@DnNo::varchar IS NULL OR starts_with(d.dn_no, @DnNo))
          AND (@CustomerId::bigint IS NULL OR d.customer_id = @CustomerId)
          AND (@Status::varchar IS NULL OR coalesce(d.status, 'OP') = @Status)
          AND (@FromDt::date IS NULL OR d.dn_dt >= @FromDt)
          AND (@ToDt::date IS NULL OR d.dn_dt <= @ToDt)
        ORDER BY d.dn_dt DESC NULLS LAST, d.dn_no DESC
        LIMIT @Limit
        """;

    private const string Find = $"""
        SELECT {HeaderColumns}
        {HeaderFrom}
        WHERE d.id = @Id AND d.type = 'IV'
        """;

    private const string Lock = "SELECT id FROM sd_sales_invoice WHERE id = @Id AND type = 'IV' FOR UPDATE";

    private const string SelectLines = """
        SELECT l.id, l.seq, coalesce(l.item_id, 0) AS item_id, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name, i.unit, l.warehouse_id,
               w.warehouse_code, l.lot_id, t.lot_no, coalesce(l.dn_qty, 0) AS dn_qty, coalesce(l.dn_prc, 0) AS dn_prc, coalesce(l.dn_amt, 0) AS dn_amt,
               l.dc_cd, coalesce(l.dc_rate, 0) AS dc_rate, i.so_usd_prc AS std_prc, l.tax_code_id, l.supply_amt, l.tax_amt, l.sales_order_line_id,
               o.so_no, ol.seq AS so_seq, l.shipment_line_id, s.ship_no, sl.seq AS ship_seq, l.shipped_qty, l.cancelled_qty
        FROM sd_sales_invoice_line l
        LEFT JOIN bc_item i ON i.id = l.item_id
        LEFT JOIN bc_warehouse w ON w.id = l.warehouse_id
        LEFT JOIN mm_lot t ON t.id = l.lot_id
        LEFT JOIN sd_sales_order_line ol ON ol.id = l.sales_order_line_id
        LEFT JOIN sd_sales_order o ON o.id = ol.sales_order_id
        LEFT JOIN sd_shipment_line sl ON sl.id = l.shipment_line_id
        LEFT JOIN sd_shipment s ON s.id = sl.shipment_id
        WHERE l.sales_invoice_id = @Id
        ORDER BY l.seq
        """;

    // Shipments and orders in id order, so two documents of the same sources cannot deadlock
    private const string LockShipments = """
        SELECT id FROM sd_shipment
        WHERE id IN (SELECT shipment_id FROM sd_shipment_line WHERE id = ANY(@Ids))
        ORDER BY id
        FOR UPDATE
        """;

    private const string SelectShipmentLines = """
        SELECT l.id, l.shipment_id, s.ship_no, l.seq, s.customer_id, s.status, s.billing_flow, s.currency_code, s.exchange_rate, l.item_id,
               l.item_name, l.warehouse_id, l.ship_qty, l.billed_qty, l.ship_prc, l.dc_cd, coalesce(l.dc_rate, 0) AS dc_rate, l.tax_code_id,
               l.sales_order_line_id
        FROM sd_shipment_line l JOIN sd_shipment s ON s.id = l.shipment_id
        WHERE l.id = ANY(@Ids)
        """;

    private const string LockOrders = """
        SELECT id FROM sd_sales_order
        WHERE id IN (SELECT sales_order_id FROM sd_sales_order_line WHERE id = ANY(@Ids))
        ORDER BY id
        FOR UPDATE
        """;

    private const string SelectOrderLines = """
        SELECT l.id, l.sales_order_id, o.so_no, l.seq, coalesce(o.customer_id, 0) AS customer_id, coalesce(o.status, 'SO') AS status, o.billing_flow,
               o.currency_code, o.exchange_rate, coalesce(l.item_id, 0) AS item_id, l.item_name, coalesce(l.so_qty, 0) AS so_qty,
               coalesce(l.dn_qty, 0) AS dn_qty, coalesce(l.so_prc, 0) AS so_prc, l.dc_cd, coalesce(l.dc_rate, 0) AS dc_rate, l.tax_code_id
        FROM sd_sales_order_line l JOIN sd_sales_order o ON o.id = l.sales_order_id
        WHERE l.id = ANY(@Ids)
        """;

    private const string InsertHeader = """
        INSERT INTO sd_sales_invoice (dn_no, customer_id, sales_order_id, dn_dt, ship_dt, due_dt, tot_dn_amt, bal_amt, remarks, terms, type, rep,
                                      ship_via, freight_charge, ship_name, ship_addr1, ship_addr2, ship_city, ship_state, ship_zip_code,
                                      ship_country, ship_phone, ship_fax, status, currency_code, exchange_rate, supply_amt, tax_amt, tot_base_amt,
                                      tax_exemption_cert_no, warehouse_id, business_place_id, billing_flow, created_by, updated_by)
        VALUES (@DnNo, @CustomerId, @SalesOrderId, @DnDt, @DnDt, @DueDt, 0, 0, @Remarks, @Terms, 'IV', @Rep, @ShipVia, @FreightCharge,
                @ShipName, @ShipAddr1, @ShipAddr2, @ShipCity, @ShipState, @ShipZipCode, @ShipCountry, @ShipPhone, @ShipFax, 'OP', @CurrencyCode,
                @ExchangeRate, 0, 0, 0, @TaxExemptionCertNo, @WarehouseId, @BusinessPlaceId, @BillingFlow, @User, @User)
        RETURNING id
        """;

    private const string UpdateHeader = """
        UPDATE sd_sales_invoice
        SET sales_order_id = @SalesOrderId, due_dt = @DueDt, warehouse_id = @WarehouseId, billing_flow = @BillingFlow, rep = @Rep, terms = @Terms,
            ship_via = @ShipVia, freight_charge = @FreightCharge, ship_name = @ShipName, ship_addr1 = @ShipAddr1, ship_addr2 = @ShipAddr2,
            ship_city = @ShipCity, ship_state = @ShipState, ship_zip_code = @ShipZipCode, ship_country = @ShipCountry, ship_phone = @ShipPhone,
            ship_fax = @ShipFax, remarks = @Remarks, tax_exemption_cert_no = @TaxExemptionCertNo, updated_at = now(), updated_by = @User
        WHERE id = @Id AND type = 'IV' AND updated_at = @UpdatedAt
        """;

    private const string DeleteLines = "DELETE FROM sd_sales_invoice_line WHERE sales_invoice_id = @Id AND id = ANY(@Ids)";

    // Location '*' until the bill-first line takes one (TASK-CORE-075); Lot '*' until a bill-first posting sets the line's lot
    private const string InsertLine = $"""
        {StockKeys.EnsureStarRows}
        INSERT INTO sd_sales_invoice_line (sales_invoice_id, seq, item_id, item_name, dn_qty, dn_prc, dn_amt, sales_order_line_id, dc_cd, dc_rate,
                                           warehouse_id, location_id, tax_code_id, supply_amt, tax_amt, shipment_line_id, lot_id, restock,
                                           created_by, updated_by)
        VALUES (@Id, @Seq, @ItemId, @ItemName, @DnQty, @DnPrc, @DnAmt, @SalesOrderLineId, @DcCd, @DcRate, @WarehouseId, {StockKeys.StarLocationId},
                @TaxCodeId, @SupplyAmt, @TaxAmt, @ShipmentLineId, coalesce(@LotId, {StockKeys.StarLotId}), false, @User, @User)
        RETURNING id
        """;

    private const string UpdateLineAmounts = """
        UPDATE sd_sales_invoice_line SET dn_amt = @DnAmt, supply_amt = @SupplyAmt, tax_amt = @TaxAmt, updated_at = now(), updated_by = @User
        WHERE sales_invoice_id = @Id AND seq = @Seq
        """;

    private const string UpdateTotals = """
        UPDATE sd_sales_invoice
        SET supply_amt = @SupplyAmt, tax_amt = @TaxAmt, tot_dn_amt = @TotDnAmt, bal_amt = @TotDnAmt, tot_base_amt = @TotBaseAmt, status = 'OP',
            updated_at = now(), updated_by = @User
        WHERE id = @Id
        """;

    private const string SetLineLot = "UPDATE sd_sales_invoice_line SET lot_id = @LotId WHERE sales_invoice_id = @Id AND seq = @Seq";

    private const string DeleteHeader = "DELETE FROM sd_sales_invoice WHERE id = @Id AND type = 'IV' AND updated_at = @UpdatedAt";

    private const string VoidHeader = """
        UPDATE sd_sales_invoice
        SET status = 'VO', supply_amt = 0, tax_amt = 0, tot_dn_amt = 0, bal_amt = 0, tot_base_amt = 0, updated_at = now(), updated_by = @User
        WHERE id = @Id AND type = 'IV' AND updated_at = @UpdatedAt
        """;

    private const string VoidLines = """
        UPDATE sd_sales_invoice_line SET dn_qty = 0, dn_amt = 0, supply_amt = 0, tax_amt = 0, updated_at = now(), updated_by = @User
        WHERE sales_invoice_id = @Id
        """;

    private const string Touch = """
        UPDATE sd_sales_invoice SET updated_at = now(), updated_by = @User
        WHERE id = @Id AND type = 'IV' AND (@UpdatedAt::timestamptz IS NULL OR updated_at = @UpdatedAt)
        """;

    private const string AddShipmentBilledQty = """
        UPDATE sd_shipment_line SET billed_qty = billed_qty + @Qty, updated_at = now(), updated_by = @User WHERE id = @Id
        """;

    private const string AddCancelledQty = """
        UPDATE sd_sales_invoice_line SET cancelled_qty = cancelled_qty + @Qty, updated_at = now(), updated_by = @User WHERE id = @Id
        """;

    private const string SelectShipmentBilling = """
        SELECT l.shipment_id, s.status, l.ship_qty, l.billed_qty
        FROM sd_shipment_line l JOIN sd_shipment s ON s.id = l.shipment_id
        WHERE l.shipment_id = ANY(@Ids)
        """;

    private const string SetShipmentStatus = "UPDATE sd_shipment SET status = @Status, updated_at = now(), updated_by = @User WHERE id = @Id";

    private const string SelectOpenTransfers = """
        SELECT m.id FROM mm_goods_movement m
        WHERE m.mov_type = 'TR' AND m.ref_key = @RefKey AND m.ref_seq = ANY(@Seqs) AND m.parent_movement_id IS NULL
          AND NOT EXISTS (SELECT 1 FROM mm_goods_movement r WHERE r.parent_movement_id = m.id)
        ORDER BY m.id
        """;

    private const string SelectApplied = """
        SELECT coalesce((SELECT sum(a.cls_amt + a.dc_amt) FROM sd_ar_clear a WHERE a.sales_invoice_id = @Id), 0) AS payment_applied,
               coalesce((SELECT sum(x.cls_amt) FROM sd_cm_clear x JOIN sd_cm_apply p ON p.id = x.cm_apply_id WHERE p.sales_invoice_id = @Id), 0)
                   AS credit_applied
        """;

    private const string SelectAvailableCredit = """
        SELECT -coalesce(sum(bal_amt), 0) FROM sd_sales_invoice
        WHERE customer_id = @CustomerId AND type = 'CM' AND coalesce(status, 'OP') <> 'VO'
        """;

    private const string CancellationColumns = """
        cm.id, cm.dn_no AS cm_no, coalesce(cm.dn_dt, cm.created_at::date) AS cm_dt, coalesce(cm.tot_dn_amt, 0) AS tot_dn_amt,
        coalesce(cm.bal_amt, 0) AS bal_amt
        """;

    private const string SelectCancellations = $"""
        SELECT {CancellationColumns}
        FROM sd_sales_invoice cm
        WHERE cm.type = 'CM'
          AND EXISTS (SELECT 1 FROM sd_sales_invoice_line x JOIN sd_sales_invoice_line l ON l.id = x.cancelled_invoice_line_id
                      WHERE x.sales_invoice_id = cm.id AND l.sales_invoice_id = @Id)
        ORDER BY cm.dn_no
        """;

    private const string LockCreditMemo = "SELECT id FROM sd_sales_invoice WHERE id = @CreditMemoId AND type = 'CM' FOR UPDATE";

    private const string SelectCancellation = $"""
        SELECT {CancellationColumns}
        FROM sd_sales_invoice cm
        WHERE cm.id = @CreditMemoId AND cm.type = 'CM'
          AND EXISTS (SELECT 1 FROM sd_sales_invoice_line x JOIN sd_sales_invoice_line l ON l.id = x.cancelled_invoice_line_id
                      WHERE x.sales_invoice_id = cm.id AND l.sales_invoice_id = @Id)
        """;

    private const string SelectCancellationLines = """
        SELECT x.seq, x.cancelled_invoice_line_id, -coalesce(x.dn_qty, 0) AS qty
        FROM sd_sales_invoice_line x
        WHERE x.sales_invoice_id = @CreditMemoId AND x.cancelled_invoice_line_id IS NOT NULL
        ORDER BY x.seq
        """;

    private const string InsertCreditMemo = """
        INSERT INTO sd_sales_invoice (dn_no, customer_id, dn_dt, ship_dt, due_dt, tot_dn_amt, bal_amt, remarks, type, status, currency_code,
                                      exchange_rate, supply_amt, tax_amt, tot_base_amt, warehouse_id, business_place_id, cm_reason01,
                                      cm_reason02, cm_reason03, cm_reason04, cm_reason05, cm_reason06, cm_reason07, cm_reason08, created_by,
                                      updated_by)
        VALUES (@CmNo, @CustomerId, @CmDt, @CmDt, @CmDt, @TotDnAmt, @TotDnAmt, @Remarks, 'CM', 'OP', @CurrencyCode, @ExchangeRate, @SupplyAmt,
                @TaxAmt, @TotBaseAmt, @WarehouseId, @BusinessPlaceId, @R1, @R2, @R3, @R4, @R5, @R6, @R7, @R8, @User, @User)
        RETURNING id
        """;

    private const string InsertCreditMemoLine = $"""
        {StockKeys.EnsureStarRows}
        INSERT INTO sd_sales_invoice_line (sales_invoice_id, seq, item_id, item_name, dn_qty, dn_prc, dn_amt, sales_order_line_id, warehouse_id,
                                           location_id, tax_code_id, supply_amt, tax_amt, lot_id, cancelled_invoice_line_id, restock, created_by,
                                           updated_by)
        VALUES (@Id, @Seq, @ItemId, @ItemName, @DnQty, @DnPrc, @DnAmt, @SalesOrderLineId, @WarehouseId, {StockKeys.StarLocationId}, @TaxCodeId,
                @SupplyAmt, @TaxAmt, coalesce(@LotId, {StockKeys.StarLotId}), @CancelledInvoiceLineId, false, @User, @User)
        """;

    private const string DeleteCreditMemo = "DELETE FROM sd_sales_invoice WHERE id = @Id AND type = 'CM'";

    // SD-BR-141; MM-BR-709: stock in the consignment warehouse is billed already, so it is not sellable
    private const string SelectOpenOrderLines = """
        SELECT o.id AS sales_order_id, o.so_no, coalesce(o.so_dt, o.created_at::date) AS so_dt, o.currency_code, o.exchange_rate, o.rep, o.terms,
               o.due_dt, o.ship_via, o.freight_charge, o.ship_name, o.ship_addr1, o.ship_addr2, o.ship_city, o.ship_state, o.ship_zip_code,
               o.ship_country, o.ship_phone, l.id AS sales_order_line_id, l.seq, l.item_id, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name,
               i.unit, coalesce(l.so_qty, 0) AS so_qty, coalesce(l.dn_qty, 0) AS dn_qty, coalesce(l.so_qty, 0) - coalesce(l.dn_qty, 0) AS remaining_qty,
               coalesce(l.so_prc, 0) AS so_prc, l.dc_cd, coalesce(l.dc_rate, 0) AS dc_rate, l.tax_code_id,
               coalesce((SELECT sum(h.onhand_qty) FROM mm_onhand h JOIN bc_warehouse w ON w.id = h.warehouse_id
                         WHERE h.item_id = l.item_id AND w.warehouse_type <> 'CONSIGNMENT'), 0) AS sellable_qty
        FROM sd_sales_order o
        JOIN sd_sales_order_line l ON l.sales_order_id = o.id
        JOIN bc_item i ON i.id = l.item_id
        WHERE o.customer_id = @CustomerId AND o.currency_code = @CurrencyCode AND o.billing_flow = 'BILL_THEN_SHIP'
          AND coalesce(o.status, 'SO') <> 'CL' AND coalesce(l.so_qty, 0) - coalesce(l.dn_qty, 0) > 0
        ORDER BY o.so_dt NULLS LAST, o.so_no, l.seq
        """;

    private const string SelectTaxLines = """
        SELECT l.seq, t.tax_kind, t.rate AS tax_rate
        FROM sd_sales_invoice_line l JOIN bc_tax_code t ON t.id = l.tax_code_id
        WHERE l.sales_invoice_id = @Id
        ORDER BY l.seq
        """;

    public async Task<IReadOnlyList<SalesInvoiceRow>> SearchAsync(
        SalesInvoiceSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. (await transaction.Connection!.QueryAsync<Row>(new CommandDefinition(
            Search,
            new
            {
                DnNo = string.IsNullOrWhiteSpace(query.DnNo) ? null : query.DnNo.Trim().ToUpperInvariant(),
                query.CustomerId,
                Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status,
                query.FromDt,
                query.ToDt,
                Limit = limit,
            },
            transaction, cancellationToken: cancellationToken))).Select(r => r.ToInvoiceRow())];

    public async Task<SalesInvoiceRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(Lock, new { Id = id }, transaction, cancellationToken: cancellationToken));
        }
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Find, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToInvoiceRow();
    }

    public async Task<IReadOnlyList<SalesInvoiceLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<SalesInvoiceLineRow>(
            new CommandDefinition(SelectLines, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<InvoiceShipmentSource>> LockShipmentLinesAsync(
        IReadOnlyCollection<long> shipmentLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new { Ids = shipmentLineIds.ToArray() };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(LockShipments, parameters, transaction, cancellationToken: cancellationToken));
        return (await transaction.Connection!.QueryAsync<InvoiceShipmentSource>(
            new CommandDefinition(SelectShipmentLines, parameters, transaction, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<InvoiceOrderSource>> LockOrderLinesAsync(
        IReadOnlyCollection<long> salesOrderLineIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new { Ids = salesOrderLineIds.ToArray() };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(LockOrders, parameters, transaction, cancellationToken: cancellationToken));
        return (await transaction.Connection!.QueryAsync<InvoiceOrderSource>(
            new CommandDefinition(SelectOrderLines, parameters, transaction, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<(long Id, IReadOnlyDictionary<int, long> LineIds)> InsertAsync(
        string dnNo, long businessPlaceId, SalesInvoiceHeader header, IReadOnlyList<SalesInvoiceLineWrite> lines, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var id = await transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            InsertHeader, HeaderParameters(header, user, new { DnNo = dnNo, BusinessPlaceId = businessPlaceId }), transaction,
            cancellationToken: cancellationToken));
        return (id, await InsertLinesAsync(id, lines, user, transaction, cancellationToken));
    }

    public async Task<IReadOnlyDictionary<int, long>?> UpdateAsync(
        long id, SalesInvoiceHeader header, IReadOnlyCollection<long> deletedLineIds, IReadOnlyList<SalesInvoiceLineWrite> added,
        DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
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
            await connection.ExecuteAsync(new CommandDefinition(
                DeleteLines, new { Id = id, Ids = deletedLineIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        }
        return await InsertLinesAsync(id, added, user, transaction, cancellationToken);
    }

    public async Task UpdateAmountsAsync(
        long id, IReadOnlyList<ShipmentInvoiceLineAmounts> amounts, ShipmentInvoiceTotals totals, string user, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        foreach (var a in amounts)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                UpdateLineAmounts, new { Id = id, a.Seq, a.DnAmt, a.SupplyAmt, a.TaxAmt, User = user }, transaction,
                cancellationToken: cancellationToken));
        }
        await connection.ExecuteAsync(new CommandDefinition(
            UpdateTotals, new { Id = id, totals.SupplyAmt, totals.TaxAmt, totals.TotDnAmt, totals.TotBaseAmt, User = user }, transaction,
            cancellationToken: cancellationToken));
    }

    public async Task SetLineLotsAsync(long id, IReadOnlyDictionary<int, long> lotIdBySeq, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var (seq, lotId) in lotIdBySeq)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                SetLineLot, new { Id = id, Seq = seq, LotId = lotId }, transaction, cancellationToken: cancellationToken));
        }
    }

    public async Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            DeleteHeader, new { Id = id, UpdatedAt = updatedAt }, transaction, cancellationToken: cancellationToken)) > 0;

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

    public async Task<bool> TouchAsync(long id, DateTimeOffset? updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Touch, new { Id = id, UpdatedAt = updatedAt, User = user }, transaction, cancellationToken: cancellationToken)) > 0;

    public Task AddShipmentBilledQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByShipmentLine, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        AddQtyAsync(AddShipmentBilledQty, qtyByShipmentLine, user, transaction, cancellationToken);

    public Task AddCancelledQtyAsync(
        IReadOnlyDictionary<long, decimal> qtyByInvoiceLine, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        AddQtyAsync(AddCancelledQty, qtyByInvoiceLine, user, transaction, cancellationToken);

    public async Task<IReadOnlyList<ShipmentBillingRow>> FindShipmentBillingAsync(
        IReadOnlyCollection<long> shipmentIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<ShipmentBillingRow>(new CommandDefinition(
            SelectShipmentBilling, new { Ids = shipmentIds.ToArray() }, transaction, cancellationToken: cancellationToken))).AsList();

    public Task SetShipmentStatusAsync(long shipmentId, string status, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SetShipmentStatus, new { Id = shipmentId, Status = status, User = user }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<long>> FindOpenTransferIdsAsync(
        string refKey, IReadOnlyCollection<int> seqs, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            SelectOpenTransfers, new { RefKey = refKey, Seqs = seqs.ToArray() }, transaction, cancellationToken: cancellationToken))).AsList();

    public Task<SalesInvoiceApplied> FindAppliedAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleAsync<SalesInvoiceApplied>(
            new CommandDefinition(SelectApplied, new { Id = id }, transaction, cancellationToken: cancellationToken));

    public Task<decimal> FindAvailableCreditAsync(long customerId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<decimal>(
            new CommandDefinition(SelectAvailableCredit, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<CancellationRow>> FindCancellationsAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<CancellationRow>(
            new CommandDefinition(SelectCancellations, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<(CancellationRow Header, IReadOnlyList<CancellationLineRow> Lines)?> FindCancellationAsync(
        long id, long creditMemoId, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        var parameters = new { Id = id, CreditMemoId = creditMemoId };
        if (forUpdate)
        {
            await connection.ExecuteAsync(new CommandDefinition(LockCreditMemo, parameters, transaction, cancellationToken: cancellationToken));
        }
        var header = await connection.QuerySingleOrDefaultAsync<CancellationRow>(
            new CommandDefinition(SelectCancellation, parameters, transaction, cancellationToken: cancellationToken));
        if (header is null)
        {
            return null;
        }
        var lines = (await connection.QueryAsync<CancellationLineRow>(
            new CommandDefinition(SelectCancellationLines, parameters, transaction, cancellationToken: cancellationToken))).AsList();
        return (header, lines);
    }

    public async Task<long> InsertCreditMemoAsync(
        string cmNo, CreditMemoHeader header, IReadOnlyList<CreditMemoLineWrite> lines, string user, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        var r = header.Reasons;
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            InsertCreditMemo,
            new
            {
                CmNo = cmNo,
                header.CustomerId,
                header.CmDt,
                header.TotDnAmt,
                header.Remarks,
                header.CurrencyCode,
                header.ExchangeRate,
                header.SupplyAmt,
                header.TaxAmt,
                header.TotBaseAmt,
                header.WarehouseId,
                header.BusinessPlaceId,
                R1 = r[0],
                R2 = r[1],
                R3 = r[2],
                R4 = r[3],
                R5 = r[4],
                R6 = r[5],
                R7 = r[6],
                R8 = r[7],
                User = user,
            },
            transaction, cancellationToken: cancellationToken));
        foreach (var l in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertCreditMemoLine,
                new
                {
                    Id = id,
                    l.Seq,
                    l.ItemId,
                    l.ItemName,
                    l.DnQty,
                    l.DnPrc,
                    l.DnAmt,
                    l.SalesOrderLineId,
                    l.WarehouseId,
                    l.TaxCodeId,
                    l.SupplyAmt,
                    l.TaxAmt,
                    l.LotId,
                    l.CancelledInvoiceLineId,
                    User = user,
                },
                transaction, cancellationToken: cancellationToken));
        }
        return id;
    }

    public Task DeleteCreditMemoAsync(long creditMemoId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            DeleteCreditMemo, new { Id = creditMemoId }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<OpenBillFirstOrderLineDto>> FindOpenOrderLinesAsync(
        long customerId, string currencyCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<OpenBillFirstOrderLineDto>(new CommandDefinition(
            SelectOpenOrderLines, new { CustomerId = customerId, CurrencyCode = currencyCode }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<SalesInvoiceTaxLine>> FindTaxLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<SalesInvoiceTaxLine>(
            new CommandDefinition(SelectTaxLines, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    private static async Task<IReadOnlyDictionary<int, long>> InsertLinesAsync(
        long id, IReadOnlyList<SalesInvoiceLineWrite> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var ids = new Dictionary<int, long>(lines.Count);
        foreach (var w in lines)
        {
            var l = w.Line;
            ids[l.Seq] = await transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
                InsertLine,
                new
                {
                    Id = id,
                    l.Seq,
                    l.ItemId,
                    l.ItemName,
                    l.DnQty,
                    l.DnPrc,
                    w.DnAmt,
                    l.SalesOrderLineId,
                    l.DcCd,
                    l.DcRate,
                    l.WarehouseId,
                    l.TaxCodeId,
                    w.SupplyAmt,
                    w.TaxAmt,
                    l.ShipmentLineId,
                    l.LotId,
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

    private static DynamicParameters HeaderParameters(SalesInvoiceHeader h, string user, object keys)
    {
        var s = h.ShipTo;
        var parameters = new DynamicParameters(new
        {
            h.CustomerId,
            h.SalesOrderId,
            h.DnDt,
            h.DueDt,
            h.CurrencyCode,
            h.ExchangeRate,
            h.WarehouseId,
            h.BillingFlow,
            h.Rep,
            h.Terms,
            h.ShipVia,
            s.FreightCharge,
            s.ShipName,
            s.ShipAddr1,
            s.ShipAddr2,
            s.ShipCity,
            s.ShipState,
            s.ShipZipCode,
            s.ShipCountry,
            s.ShipPhone,
            s.ShipFax,
            h.Remarks,
            h.TaxExemptionCertNo,
            User = user,
        });
        parameters.AddDynamicParams(keys);
        return parameters;
    }

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id, string DnNo, string Status, string BillingFlow, long CustomerId, string? CustomerName, long? SalesOrderId, string? SoNo,
        DateOnly DnDt, DateOnly? DueDt, long WarehouseId, long? BusinessPlaceId, string CurrencyCode, decimal ExchangeRate, string? Rep,
        string? Terms, string? ShipVia, string? FreightCharge, string? ShipName, string? ShipAddr1, string? ShipAddr2, string? ShipCity,
        string? ShipState, string? ShipZipCode, string? ShipCountry, string? ShipPhone, string? ShipFax, string? Remarks,
        string? TaxExemptionCertNo, decimal SupplyAmt, decimal TaxAmt, decimal TotDnAmt, decimal TotBaseAmt, decimal BalAmt, long? ShipmentId,
        string? ShipNo, DateTime UpdatedAt, DateTime? CreatedAt, string? CreatedBy, string? UpdatedBy)
    {
        public SalesInvoiceRow ToInvoiceRow() => new(
            Id, DnNo, Status, BillingFlow, CustomerId, CustomerName, SalesOrderId, SoNo, DnDt, DueDt, WarehouseId, BusinessPlaceId, CurrencyCode,
            ExchangeRate, Rep, Terms, ShipVia,
            new SalesOrderShipTo(ShipName, ShipAddr1, ShipAddr2, ShipCity, ShipState, ShipZipCode, ShipCountry, ShipPhone, ShipFax, FreightCharge),
            Remarks, TaxExemptionCertNo, SupplyAmt, TaxAmt, TotDnAmt, TotBaseAmt, BalAmt, ShipmentId, ShipNo, Utc(UpdatedAt),
            CreatedAt is { } c ? Utc(c) : null, CreatedBy, UpdatedBy);

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

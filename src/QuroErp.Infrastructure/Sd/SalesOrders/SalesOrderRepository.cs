using System.Data;
using Dapper;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Infrastructure.Sd.SalesOrders;

internal sealed class SalesOrderRepository : ISalesOrderRepository
{
    // Migrated rows may lack a status, date or amounts; they read as SO with zero amounts
    private const string HeaderColumns = """
        o.id, o.so_no, coalesce(o.status, 'SO') AS status, coalesce(o.customer_id, 0) AS customer_id, c.c_name AS customer_name,
        coalesce(o.so_dt, o.created_at::date) AS so_dt, o.exp_ship_dt, o.due_dt, o.currency_code, o.exchange_rate, o.billing_flow,
        o.rep, o.terms, o.ship_via, o.ship_name, o.ship_addr1, o.ship_addr2, o.ship_city, o.ship_state, o.ship_zip_code, o.ship_country,
        o.ship_phone, o.ship_fax, o.freight_charge, o.remarks, c.dc_status, o.supply_amt, o.tax_amt, coalesce(o.tot_so_amt, 0) AS tot_so_amt,
        o.tot_base_amt, o.updated_at, o.created_at, o.created_by, o.updated_by
        """;

    private const string Search = $"""
        SELECT {HeaderColumns}
        FROM sd_sales_order o LEFT JOIN bc_customer c ON c.id = o.customer_id
        WHERE (@SoNo::varchar IS NULL OR starts_with(o.so_no, @SoNo))
          AND (@CustomerId::bigint IS NULL OR o.customer_id = @CustomerId)
          AND (@CustomerNamePattern::text IS NULL OR c.c_name ILIKE @CustomerNamePattern ESCAPE '\')
          AND (@Status::varchar IS NULL OR coalesce(o.status, 'SO') = @Status)
          AND (@FromDt::date IS NULL OR o.so_dt >= @FromDt)
          AND (@ToDt::date IS NULL OR o.so_dt <= @ToDt)
        ORDER BY o.so_dt DESC NULLS LAST, o.so_no DESC
        LIMIT @Limit
        """;

    private const string Find = $"""
        SELECT {HeaderColumns}
        FROM sd_sales_order o LEFT JOIN bc_customer c ON c.id = o.customer_id
        WHERE o.id = @Id
        """;

    private const string Lock = "SELECT id FROM sd_sales_order WHERE id = @Id FOR UPDATE";

    private const string SelectLines = """
        SELECT l.id, l.seq, coalesce(l.item_id, 0) AS item_id, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name,
               coalesce(l.so_qty, 0) AS so_qty, coalesce(l.so_prc, 0) AS so_prc, coalesce(l.so_amt, 0) AS so_amt, l.dc_cd,
               coalesce(l.dc_rate, 0) AS dc_rate, i.so_usd_prc AS std_prc, l.tax_code_id, l.supply_amt, l.tax_amt, l.ship_qty,
               coalesce(l.dn_qty, 0) AS dn_qty,
               (SELECT max(d.dn_no) FROM sd_sales_invoice_line dl JOIN sd_sales_invoice d ON d.id = dl.sales_invoice_id
                WHERE dl.sales_order_line_id = l.id) AS inv_no,
               i.unit
        FROM sd_sales_order_line l LEFT JOIN bc_item i ON i.id = l.item_id
        WHERE l.sales_order_id = @Id
        ORDER BY l.seq
        """;

    private const string ItemColumns = """
        SELECT i.id, i.item_cd, i.item_name, i.type AS item_type, i.unit, i.valid_flag, i.taxable, i.vat_kind, i.so_usd_prc AS std_prc,
               p.sales_prc AS level_prc, i.upc_code
        FROM bc_item i LEFT JOIN bc_sales_price p ON p.item_id = i.id AND p.price_level = @PriceLevel
        """;

    private const string SelectItemsById = ItemColumns + " WHERE i.id = ANY(@Ids)";

    private const string SelectItemsByCode = ItemColumns + " WHERE i.item_cd = ANY(@Keys)";

    // upc_code is unique (MM-BR-817)
    private const string SelectItemsByBarcode = ItemColumns + " WHERE i.upc_code = ANY(@Keys)";

    private const string SelectDiscountRates = "SELECT discount_code, rate FROM sd_discount_rate";

    private const string SelectTaxCodeOfKind = "SELECT id FROM bc_tax_code WHERE tax_kind = @TaxKind AND use_yn ORDER BY id LIMIT 1";

    private const string SelectHasShipmentOrInvoice = """
        SELECT EXISTS (SELECT 1 FROM sd_shipment WHERE sales_order_id = @Id AND status <> 'VO')
            OR EXISTS (SELECT 1 FROM sd_sales_invoice WHERE sales_order_id = @Id AND coalesce(status, '') <> 'VO')
        """;

    private const string UpdateCustomerShipTo = """
        UPDATE bc_customer
        SET ship_name = @ShipName, ship_addr1 = @ShipAddr1, ship_addr2 = @ShipAddr2, ship_city = @ShipCity, ship_state = @ShipState,
            ship_zip_code = @ShipZipCode, ship_country = @ShipCountry, ship_phone = @ShipPhone, ship_fax = @ShipFax,
            freight_charge = @FreightCharge, updated_at = now(), updated_by = @User
        WHERE id = @CustomerId
        """;

    // One row per level and item (SD-BR-403); the sales price screen locks the level the same way
    private const string LockLevel = "SELECT pg_advisory_xact_lock(hashtext('bc_sales_price:' || @PriceLevel))";

    private const string UpdatePrice = """
        UPDATE bc_sales_price SET sales_prc = @SalesPrc, updated_at = now(), updated_by = @User
        WHERE price_level = @PriceLevel AND item_id = @ItemId
        """;

    private const string InsertPrice = """
        INSERT INTO bc_sales_price (price_level, item_id, sales_prc, created_by, updated_by)
        VALUES (@PriceLevel, @ItemId, @SalesPrc, @User, @User)
        """;

    private const string InsertHeader = """
        INSERT INTO sd_sales_order (so_no, status, customer_id, so_dt, exp_ship_dt, due_dt, currency_code, exchange_rate, billing_flow, rep,
                                    terms, ship_via, freight_charge, ship_name, ship_addr1, ship_addr2, ship_city, ship_state, ship_zip_code,
                                    ship_country, ship_phone, ship_fax, remarks, supply_amt, tax_amt, tot_so_amt, tot_base_amt,
                                    business_place_id, created_by, updated_by)
        VALUES (@SoNo, 'SO', @CustomerId, @SoDt, @ExpShipDt, @DueDt, @CurrencyCode, @ExchangeRate, @BillingFlow, @Rep, @Terms, @ShipVia,
                @FreightCharge, @ShipName, @ShipAddr1, @ShipAddr2, @ShipCity, @ShipState, @ShipZipCode, @ShipCountry, @ShipPhone, @ShipFax,
                @Remarks, @SupplyAmt, @TaxAmt, @TotSoAmt, @TotBaseAmt, @BusinessPlaceId, @User, @User)
        RETURNING id
        """;

    private const string UpdateHeader = """
        UPDATE sd_sales_order
        SET so_dt = @SoDt, exp_ship_dt = @ExpShipDt, due_dt = @DueDt, currency_code = @CurrencyCode, exchange_rate = @ExchangeRate,
            billing_flow = @BillingFlow, rep = @Rep, terms = @Terms, ship_via = @ShipVia, freight_charge = @FreightCharge,
            ship_name = @ShipName, ship_addr1 = @ShipAddr1, ship_addr2 = @ShipAddr2, ship_city = @ShipCity, ship_state = @ShipState,
            ship_zip_code = @ShipZipCode, ship_country = @ShipCountry, ship_phone = @ShipPhone, ship_fax = @ShipFax, remarks = @Remarks,
            supply_amt = @SupplyAmt, tax_amt = @TaxAmt, tot_so_amt = @TotSoAmt, tot_base_amt = @TotBaseAmt, updated_at = now(),
            updated_by = @User
        WHERE id = @Id AND updated_at = @UpdatedAt
        """;

    private const string DeleteMissingLines = "DELETE FROM sd_sales_order_line WHERE sales_order_id = @Id AND NOT (id = ANY(@KeepIds))";

    // Kept lines are renumbered; moving them out of the way first keeps (sales_order_id, seq) unique
    private const string MoveSeqAside = "UPDATE sd_sales_order_line SET seq = -seq WHERE sales_order_id = @Id";

    private const string UpdateLine = """
        UPDATE sd_sales_order_line
        SET seq = @Seq, item_id = @ItemId, item_name = @ItemName, so_qty = @SoQty, so_prc = @SoPrc, so_amt = @SoAmt, dc_cd = @DcCd,
            dc_rate = @DcRate, tax_code_id = @TaxCodeId, supply_amt = @SupplyAmt, tax_amt = @TaxAmt, updated_at = now(), updated_by = @User
        WHERE id = @LineId AND sales_order_id = @Id
        """;

    private const string InsertLine = """
        INSERT INTO sd_sales_order_line (sales_order_id, seq, item_id, item_name, so_qty, so_prc, so_amt, dc_cd, dc_rate, tax_code_id,
                                         supply_amt, tax_amt, dn_qty, created_by, updated_by)
        VALUES (@Id, @Seq, @ItemId, @ItemName, @SoQty, @SoPrc, @SoAmt, @DcCd, @DcRate, @TaxCodeId, @SupplyAmt, @TaxAmt, 0, @User, @User)
        """;

    // Lines go with the header (ON DELETE CASCADE)
    private const string Delete = "DELETE FROM sd_sales_order WHERE id = @Id AND updated_at = @UpdatedAt";

    private const string SelectPicking = """
        SELECT l.seq, i.item_cd, coalesce(l.item_name, i.item_name) AS item_name, i.unit, i.location, coalesce(l.so_qty, 0) AS so_qty
        FROM sd_sales_order_line l LEFT JOIN bc_item i ON i.id = l.item_id
        WHERE l.sales_order_id = @Id
        ORDER BY i.location NULLS LAST, l.seq
        """;

    private const string SelectSetComponents = """
        SELECT l.seq, s.item_cd AS set_item_cd, coalesce(l.item_name, s.item_name) AS set_item_name, coalesce(l.so_qty, 0) AS so_qty,
               ci.item_cd AS component_cd, ci.item_name AS component_name, ci.unit, ci.location, b.child_qty
        FROM sd_sales_order_line l
        JOIN bc_item s ON s.id = l.item_id AND s.type IN ('KIT', 'SET')
        JOIN sd_bom b ON b.parent_item_id = s.id
        LEFT JOIN bc_item ci ON ci.id = b.child_item_id
        WHERE l.sales_order_id = @Id
        ORDER BY l.seq, b.seq
        """;

    public async Task<IReadOnlyList<SalesOrderRow>> SearchAsync(
        SalesOrderSearchQuery query, int limit, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. (await transaction.Connection!.QueryAsync<Row>(new CommandDefinition(
            Search,
            new
            {
                SoNo = string.IsNullOrWhiteSpace(query.SoNo) ? null : query.SoNo.Trim().ToUpperInvariant(),
                query.CustomerId,
                CustomerNamePattern = string.IsNullOrWhiteSpace(query.CustomerName) ? null : "%" + EscapeLike(query.CustomerName.Trim()) + "%",
                Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status,
                query.FromDt,
                query.ToDt,
                Limit = limit,
            },
            transaction, cancellationToken: cancellationToken))).Select(r => r.ToSalesOrderRow())];

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    public async Task<SalesOrderRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(Lock, new { Id = id }, transaction, cancellationToken: cancellationToken));
        }
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Find, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToSalesOrderRow();
    }

    public async Task<IReadOnlyList<SalesOrderLineRow>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<SalesOrderLineRow>(
            new CommandDefinition(SelectLines, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<SalesOrderItemRow>> FindItemsByIdAsync(
        IReadOnlyCollection<long> itemIds, string? priceLevel, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<SalesOrderItemRow>(new CommandDefinition(
            SelectItemsById, new { Ids = itemIds.ToArray(), PriceLevel = priceLevel }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<SalesOrderItemRow>> FindItemsByKeyAsync(
        IReadOnlyCollection<string> keys, bool byBarcode, string? priceLevel, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<SalesOrderItemRow>(new CommandDefinition(
            byBarcode ? SelectItemsByBarcode : SelectItemsByCode, new { Keys = keys.ToArray(), PriceLevel = priceLevel }, transaction,
            cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyDictionary<string, decimal>> FindDiscountRatesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(string Code, decimal Rate)>(
            new CommandDefinition(SelectDiscountRates, transaction: transaction, cancellationToken: cancellationToken)))
        .ToDictionary(r => r.Code, r => r.Rate, StringComparer.Ordinal);

    public Task<long?> FindTaxCodeIdOfKindAsync(string taxKind, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(SelectTaxCodeOfKind, new { TaxKind = taxKind }, transaction, cancellationToken: cancellationToken));

    public Task<bool> HasShipmentOrInvoiceAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectHasShipmentOrInvoice, new { Id = id }, transaction, cancellationToken: cancellationToken));

    public Task UpdateCustomerShipToAsync(
        long customerId, SalesOrderShipTo shipTo, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateCustomerShipTo,
            new
            {
                CustomerId = customerId,
                shipTo.ShipName,
                shipTo.ShipAddr1,
                shipTo.ShipAddr2,
                shipTo.ShipCity,
                shipTo.ShipState,
                shipTo.ShipZipCode,
                shipTo.ShipCountry,
                shipTo.ShipPhone,
                shipTo.ShipFax,
                shipTo.FreightCharge,
                User = user,
            },
            transaction, cancellationToken: cancellationToken));

    public async Task UpsertPriceLevelAsync(
        string priceLevel, IReadOnlyList<PriceLevelChange> prices, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        await connection.ExecuteAsync(new CommandDefinition(LockLevel, new { PriceLevel = priceLevel }, transaction, cancellationToken: cancellationToken));
        foreach (var price in prices)
        {
            var parameters = new { PriceLevel = priceLevel, price.ItemId, price.SalesPrc, User = user };
            if (await connection.ExecuteAsync(new CommandDefinition(UpdatePrice, parameters, transaction, cancellationToken: cancellationToken)) == 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(InsertPrice, parameters, transaction, cancellationToken: cancellationToken));
            }
        }
    }

    public async Task<long> InsertAsync(
        string soNo, long businessPlaceId, SalesOrderWrite order, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var id = await transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            InsertHeader, HeaderParameters(order, user, new { SoNo = soNo, BusinessPlaceId = businessPlaceId }), transaction,
            cancellationToken: cancellationToken));
        await InsertLinesAsync(id, order.Lines, user, transaction, cancellationToken);
        return id;
    }

    public async Task<bool> UpdateAsync(
        long id, SalesOrderWrite order, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
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

    public async Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Id = id, UpdatedAt = updatedAt }, transaction, cancellationToken: cancellationToken)) == 1;

    public async Task<IReadOnlyList<SalesOrderPickingRow>> FindPickingAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<SalesOrderPickingRow>(
            new CommandDefinition(SelectPicking, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<SalesOrderSetComponentRow>> FindSetComponentsAsync(
        long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<SalesOrderSetComponentRow>(
            new CommandDefinition(SelectSetComponents, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();

    private static async Task InsertLinesAsync(
        long id, IEnumerable<SalesOrderLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            await transaction.Connection!.ExecuteAsync(new CommandDefinition(
                InsertLine, LineParameters(id, line, user), transaction, cancellationToken: cancellationToken));
        }
    }

    private static DynamicParameters HeaderParameters(SalesOrderWrite order, string user, object keys)
    {
        var h = order.Header;
        var s = h.ShipTo;
        var t = order.Totals;
        var parameters = new DynamicParameters(new
        {
            h.CustomerId,
            h.SoDt,
            h.ExpShipDt,
            h.DueDt,
            h.CurrencyCode,
            h.ExchangeRate,
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
            t.SupplyAmt,
            t.TaxAmt,
            t.TotSoAmt,
            t.TotBaseAmt,
            User = user,
        });
        parameters.AddDynamicParams(keys);
        return parameters;
    }

    private static object LineParameters(long id, SalesOrderLine l, string user) => new
    {
        Id = id,
        LineId = l.Id,
        l.Seq,
        l.ItemId,
        l.ItemName,
        l.SoQty,
        l.SoPrc,
        l.SoAmt,
        l.DcCd,
        l.DcRate,
        l.TaxCodeId,
        l.SupplyAmt,
        l.TaxAmt,
        User = user,
    };

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id, string SoNo, string Status, long CustomerId, string? CustomerName, DateOnly SoDt, DateOnly? ExpShipDt, DateOnly? DueDt,
        string CurrencyCode, decimal ExchangeRate, string BillingFlow, string? Rep, string? Terms, string? ShipVia, string? ShipName,
        string? ShipAddr1, string? ShipAddr2, string? ShipCity, string? ShipState, string? ShipZipCode, string? ShipCountry, string? ShipPhone,
        string? ShipFax, string? FreightCharge, string? Remarks, string? DcStatus, decimal SupplyAmt, decimal TaxAmt, decimal TotSoAmt,
        decimal TotBaseAmt, DateTime UpdatedAt, DateTime? CreatedAt, string? CreatedBy, string? UpdatedBy)
    {
        public SalesOrderRow ToSalesOrderRow() => new(
            Id, SoNo, Status, CustomerId, CustomerName, SoDt, ExpShipDt, DueDt, CurrencyCode, ExchangeRate, BillingFlow, Rep, Terms, ShipVia,
            new SalesOrderShipTo(ShipName, ShipAddr1, ShipAddr2, ShipCity, ShipState, ShipZipCode, ShipCountry, ShipPhone, ShipFax, FreightCharge),
            Remarks, DcStatus, SupplyAmt, TaxAmt, TotSoAmt, TotBaseAmt, Utc(UpdatedAt), CreatedAt is { } c ? Utc(c) : null, CreatedBy, UpdatedBy);

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

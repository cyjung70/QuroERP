using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Lots;
using QuroErp.Application.Sd.Assemblies;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Contracts.Sd.Assemblies;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Lots;

/// <summary>Own schema for the Lot trace tests: the fixtures commit, so their rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class LotTraceSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "LotTraceSchema";
}

/// <summary>
/// Lot trace on real PostgreSQL (MM-UC-040, TASK-MM-063). Each test makes its own Lot-managed item, vendor and two customers,
/// receives a Lot of the item on a receipt document in the default warehouse on <see cref="Day"/>, and ships or bills it with
/// document rows written here and stock posted by the inventory service, so the trace reads what the documents leave behind.
/// </summary>
[Collection(LotTraceSchemaDefinition.Name)]
public class LotTraceServiceTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Day = new(2026, 11, 4);

    private LotTraceService Service => new(db.Get<ILotTraceRepository>(), db.Get<IUnitOfWorkFactory>());

    private InventoryService Inventory => new(
        db.Get<IInventoryRepository>(), db.Get<IInventorySettingRepository>(), db.Get<ISetComponentRepository>(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    private AssemblyService Assemblies => new(
        db.Get<IAssemblyRepository>(), db.Get<IInventoryRepository>(), db.Get<ISetComponentRepository>(), db.Get<ILocalizationRepository>(),
        Inventory, new DocumentNumberService(db.Get<IDocumentNumberRepository>()), new FixedClock(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "MM-UC-040-T01"), Trait("Id", "MM-UT-366"), Trait("Rule", "MM-BR-366")]
    public async Task Forward_FromAReceivedLot_ShowsItsReceiptAndTheShipmentsToTwoCustomers_WithoutAReversedOne()
    {
        var m = await MastersAsync();
        var lot = await ReceiveAsync(m, 10m);
        var a = await ShipAsync(m.CustomerA, m.Item, lot.Id, 3m, Day.AddDays(1));
        var b = await ShipAsync(m.CustomerB, m.Item, lot.Id, 4m, Day.AddDays(2));
        var undone = await ShipAsync(m.CustomerA, m.Item, lot.Id, 1m, Day.AddDays(2));
        await ReverseAsync(undone.MovementIds, Day.AddDays(3));

        var rows = await Service.ForwardAsync(new LotTraceForwardQuery { ItemId = m.Item, LotNo = lot.LotNo }, None);

        (int, string, string?, string?, string?, decimal?)[] expected =
        [
            (0, "LOT", lot.GrNo, null, m.VendorName, null),
            (1, "GR", lot.GrNo, "GR", m.VendorName, 10m),
            (1, "SHIP", a.DocNo, "DN", m.CustomerAName, -3m),
            (1, "SHIP", b.DocNo, "DN", m.CustomerBName, -4m),
        ];
        rows.Select(r => (r.Level, r.DocType, r.DocNo, r.MovType, r.PartyName, r.Qty)).Should().Equal(expected);
        rows.Skip(1).Should().OnlyContain(r => r.ParentKey == 1);
        (await Service.ForwardAsync(new LotTraceForwardQuery { ItemId = m.Item, LotNo = "*" }, None)).Should().BeEmpty();
        (await Service.ForwardAsync(new LotTraceForwardQuery { ItemId = m.Item, LotNo = "NONE" }, None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "MM-UC-040-T02"), Trait("Id", "MM-UT-367"), Trait("Rule", "MM-BR-367")]
    public async Task Backward_FromABillFirstInvoiceLine_ShowsTheIssue_ThenTheReceiptAndVendor()
    {
        var m = await MastersAsync();
        var lot = await ReceiveAsync(m, 10m);
        var invoice = await BillFirstAsync(m, m.CustomerA, lot.Id, 2m, Day.AddDays(1));

        var byLine = await Service.BackwardAsync(new LotTraceBackwardQuery { InvoiceLineId = invoice.LineId }, None);
        var byNumber = await Service.BackwardAsync(new LotTraceBackwardQuery { DocNo = invoice.DocNo }, None);
        var byCustomer = await Service.BackwardAsync(
            new LotTraceBackwardQuery { CustomerId = m.CustomerA, From = Day, To = Day.AddDays(5) }, None);

        (int, string, string?, string?, string?, decimal?)[] expected =
        [
            (0, "INV", invoice.DocNo, "TR", m.CustomerAName, -2m),
            (1, "GR", lot.GrNo, null, m.VendorName, 10m),
        ];
        byLine.Select(r => (r.Level, r.DocType, r.DocNo, r.MovType, r.PartyName, r.Qty)).Should().Equal(expected);
        byNumber.Should().Equal(byLine);
        byCustomer.Should().Equal(byLine);
        byLine[1].ParentKey.Should().Be(1);
    }

    [DatabaseFact, Trait("Id", "MM-UC-040-T02"), Trait("Id", "MM-UT-367"), Trait("Rule", "MM-BR-367")]
    public async Task Backward_FromAShipmentLine_AndACustomerWithoutAPeriod()
    {
        var m = await MastersAsync();
        var lot = await ReceiveAsync(m, 10m);
        var shipment = await ShipAsync(m.CustomerB, m.Item, lot.Id, 4m, Day.AddDays(1));

        var rows = await Service.BackwardAsync(new LotTraceBackwardQuery { ShipmentLineId = shipment.LineId }, None);
        var act = () => Service.BackwardAsync(new LotTraceBackwardQuery { CustomerId = m.CustomerB, To = Day }, None);

        rows.Select(r => (r.DocType, r.DocNo, r.Qty)).Should().Equal(("SHIP", shipment.DocNo, (decimal?)-4m), ("GR", lot.GrNo, 10m));
        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args).Should().BeEquivalentTo(("Common-E-005", new object[] { "from" }));
    }

    [DatabaseFact, Trait("Id", "MM-UC-040-T03"), Trait("Id", "MM-UT-368"), Trait("Rule", "MM-BR-368")]
    public async Task SetLot_IsReachedForwardFromItsComponentLot_AndBackwardFromItsShipment()
    {
        var m = await MastersAsync();
        var lot = await ReceiveAsync(m, 10m);
        var set = await SetAsync(m);
        var assembly = await Assemblies.AssembleAsync(new SaveAssemblyRequest(set, 3m, Day.AddDays(1), m.Main, null), "kim", None);
        var setLotNo = assembly.SetLotNo!;
        var setLot = (await QueryAsync<long>("SELECT id FROM mm_lot WHERE assembly_id = @Id", new { assembly.Id })).Single();
        var shipment = await ShipAsync(m.CustomerB, set, setLot, 2m, Day.AddDays(2));

        var forward = await Service.ForwardAsync(new LotTraceForwardQuery { ItemId = m.Item, LotNo = lot.LotNo }, None);
        var backward = await Service.BackwardAsync(new LotTraceBackwardQuery { DocNo = shipment.DocNo }, None);

        (int, int?, string, string?, string, decimal?)[] forwardRows =
        [
            (1, null, "LOT", lot.GrNo, lot.LotNo, null),
            (2, 1, "GR", lot.GrNo, lot.LotNo, 10m),
            (3, 1, "ASM", assembly.AssemblyNo, lot.LotNo, -6m),
            (4, 3, "LOT", assembly.AssemblyNo, setLotNo, 6m),
            (5, 4, "ASM", assembly.AssemblyNo, setLotNo, 3m),
            (6, 4, "SHIP", shipment.DocNo, setLotNo, -2m),
        ];
        forward.Select(r => (r.Key, r.ParentKey, r.DocType, r.DocNo, r.LotNo, r.Qty)).Should().Equal(forwardRows);
        (int, string, string?, string, decimal?)[] backwardRows =
        [
            (0, "SHIP", shipment.DocNo, setLotNo, -2m),
            (1, "ASM", assembly.AssemblyNo, setLotNo, 3m),
            (2, "GR", lot.GrNo, lot.LotNo, 6m),
        ];
        backward.Select(r => (r.Level, r.DocType, r.DocNo, r.LotNo, r.Qty)).Should().Equal(backwardRows);
    }

    private sealed record Masters(
        string Key, long Item, long Main, long Consign, long NoTax, long Vendor, string VendorName, long CustomerA, string CustomerAName,
        long CustomerB, string CustomerBName);

    private sealed record ReceivedLot(long Id, string LotNo, string GrNo);

    private sealed record Issue(string DocNo, long LineId, IReadOnlyList<long> MovementIds);

    // A Lot-managed item, a vendor and two customers; the default and consignment warehouses and the tax code NONE
    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Item, long Main, long Consign, long NoTax, long Vendor, long CustomerA, long CustomerB)>(
            """
            WITH i AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, created_by, updated_by)
                       VALUES ('LT' || @Key, 'Cable', 'INVENTORY', true, DATE '2026-10-01', 't', 't') RETURNING id),
                 v AS (INSERT INTO bc_vendor (v_name, created_by, updated_by) VALUES ('Vendor ' || @Key, 't', 't') RETURNING id),
                 a AS (INSERT INTO bc_customer (c_name, created_by, updated_by) VALUES ('Customer A ' || @Key, 't', 't') RETURNING id),
                 b AS (INSERT INTO bc_customer (c_name, created_by, updated_by) VALUES ('Customer B ' || @Key, 't', 't') RETURNING id)
            SELECT (SELECT id FROM i), (SELECT id FROM bc_warehouse WHERE is_default),
                   (SELECT id FROM bc_warehouse WHERE warehouse_code = 'CONSIGN'), (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'),
                   (SELECT id FROM v), (SELECT id FROM a), (SELECT id FROM b)
            """,
            new { Key = key });
        await connection.ExecuteAsync(StarKeys.Ensure("@Main, @Consign", "@Item"), new { ids.Main, ids.Consign, ids.Item });
        return new Masters(
            key, ids.Item, ids.Main, ids.Consign, ids.NoTax, ids.Vendor, "Vendor " + key, ids.CustomerA, "Customer A " + key, ids.CustomerB,
            "Customer B " + key);
    }

    // A Lot-managed Set of 2 of the item per Set
    private async Task<long> SetAsync(Masters m)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var set = await connection.QuerySingleAsync<long>(
            """
            WITH s AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, created_by, updated_by)
                       VALUES ('ST' || @Key, 'Cable Set', 'SET', true, DATE '2026-10-01', 't', 't') RETURNING id),
                 b AS (INSERT INTO sd_bom (parent_item_id, seq, child_item_id, child_qty, created_by, updated_by)
                       SELECT id, 1, @Item, 2, 't', 't' FROM s)
            SELECT id FROM s
            """,
            new { m.Key, m.Item });
        await connection.ExecuteAsync(StarKeys.Ensure("@Main", "@Set"), new { m.Main, Set = set });
        return set;
    }

    // A receipt document of one line and its stock movement; the Lot it makes points back to the line (MM-BR-353)
    private async Task<ReceivedLot> ReceiveAsync(Masters m, decimal qty)
    {
        var grNo = "GR" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var lotNo = "L" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        // The movement makes the Lot first: a receipt line of an item must name its Lot (ck_mm_goods_receipt_line_lot_id)
        var posted = await PostAsync(new StockMovementRequest("GR", Day, m.Item, m.Main, qty, 5m, grNo, 1, LotNo: lotNo));
        var lotId = posted[0].LotId!.Value;
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var line = await connection.QuerySingleAsync<long>(
            $"""
            WITH r AS (INSERT INTO mm_goods_receipt (gr_no, vendor_id, gr_dt, warehouse_id, currency_code, exchange_rate, business_place_id,
                                                     created_by, updated_by)
                       SELECT @GrNo, @Vendor, @Day, @Main, 'USD', 1, id, 't', 't' FROM bc_business_place WHERE is_default RETURNING id)
            INSERT INTO mm_goods_receipt_line (goods_receipt_id, seq, item_id, gr_qty, gr_prc, gr_amt, warehouse_id, location_id, lot_id,
                                               tax_code_id, created_by, updated_by)
            SELECT id, 1, @Item, @Qty, 5, @Qty * 5, @Main, {StarKeys.Location("@Main")}, @Lot, @NoTax, 't', 't' FROM r
            RETURNING id
            """,
            new { GrNo = grNo, m.Vendor, Day, m.Main, m.Item, Qty = qty, Lot = lotId, m.NoTax });
        await connection.ExecuteAsync(
            "UPDATE mm_lot SET goods_receipt_line_id = @Line WHERE id = @Lot", new { Line = line, Lot = lotId });
        return new ReceivedLot(lotId, lotNo, grNo);
    }

    // A shipment of one line from the default warehouse and its delivery movement
    private async Task<Issue> ShipAsync(long customer, long item, long lotId, decimal qty, DateOnly date)
    {
        var shipNo = "SH" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var main = await MainAsync(connection);
        var line = await connection.QuerySingleAsync<long>(
            $"""
            WITH s AS (INSERT INTO sd_shipment (ship_no, customer_id, ship_dt, warehouse_id, currency_code, exchange_rate, business_place_id,
                                                created_by, updated_by)
                       SELECT @ShipNo, @Customer, @Date, @Main, 'USD', 1, id, 't', 't' FROM bc_business_place WHERE is_default RETURNING id)
            INSERT INTO sd_shipment_line (shipment_id, seq, item_id, warehouse_id, ship_qty, ship_prc, location_id, lot_id, tax_code_id,
                                          created_by, updated_by)
            SELECT id, 1, @Item, @Main, @Qty, 9, {StarKeys.Location("@Main")}, @Lot, (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), 't', 't'
            FROM s
            RETURNING id
            """,
            new { ShipNo = shipNo, Customer = customer, Date = date, Main = main, Item = item, Qty = qty, Lot = lotId });
        var posted = await PostAsync(new StockMovementRequest("DN", date, item, main, -qty, null, shipNo, 1, lotId));
        return new Issue(shipNo, line, [.. posted.Select(p => p.Id)]);
    }

    // A bill-first invoice of one line: its stock leaves the default warehouse for the consignment warehouse under its number
    private async Task<Issue> BillFirstAsync(Masters m, long customer, long lotId, decimal qty, DateOnly date)
    {
        var dnNo = "DN" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var line = await connection.QuerySingleAsync<long>(
            $"""
            WITH d AS (INSERT INTO sd_sales_invoice (dn_no, customer_id, dn_dt, tot_dn_amt, bal_amt, type, status, currency_code, exchange_rate,
                                                     warehouse_id, business_place_id, billing_flow, created_by, updated_by)
                       SELECT @DnNo, @Customer, @Date, @Qty * 9, @Qty * 9, 'IV', 'OP', 'USD', 1, @Main, id, 'BILL_THEN_SHIP', 't', 't'
                       FROM bc_business_place WHERE is_default RETURNING id)
            INSERT INTO sd_sales_invoice_line (sales_invoice_id, seq, item_id, dn_qty, dn_prc, dn_amt, warehouse_id, location_id, lot_id,
                                               tax_code_id, created_by, updated_by)
            SELECT id, 1, @Item, @Qty, 9, @Qty * 9, @Main, {StarKeys.Location("@Main")}, @Lot, @NoTax, 't', 't' FROM d
            RETURNING id
            """,
            new { DnNo = dnNo, Customer = customer, Date = date, Qty = qty, m.Main, m.Item, Lot = lotId, m.NoTax });
        var issued = await PostAsync(new StockMovementRequest("TR", date, m.Item, m.Main, -qty, null, dnNo, 1, lotId));
        await PostAsync(new StockMovementRequest("TR", date, m.Item, m.Consign, qty, null, dnNo, 1, lotId));
        return new Issue(dnNo, line, [.. issued.Select(p => p.Id)]);
    }

    private async Task<IReadOnlyList<GoodsMovement>> PostAsync(StockMovementRequest request)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var posted = await Inventory.PostAsync([request], "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
        return posted;
    }

    private async Task ReverseAsync(IReadOnlyList<long> movementIds, DateOnly date)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await Inventory.ReverseAsync(movementIds, date, "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
    }

    private static Task<long> MainAsync(NpgsqlConnection connection) =>
        connection.QuerySingleAsync<long>("SELECT id FROM bc_warehouse WHERE is_default");

    private async Task<List<T>> QueryAsync<T>(string sql, object parameters)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return [.. await connection.QueryAsync<T>(sql, parameters)];
    }

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => new(2026, 11, 10);

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}

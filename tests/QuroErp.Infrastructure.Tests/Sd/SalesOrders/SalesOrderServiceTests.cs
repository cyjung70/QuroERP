using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Sd.Customers;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Domain.Common;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Sd.SalesOrders;

/// <summary>Own schema for sales order tests: the service commits, so its rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class SalesOrderSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "SalesOrderSchema";
}

/// <summary>
/// SalesOrderService with the Dapper repositories on real PostgreSQL (SD-UC-002). Without a company the base currency is USD and
/// the country is US. Each test makes its own customer, items and discount code.
/// </summary>
[Collection(SalesOrderSchemaDefinition.Name)]
public partial class SalesOrderServiceTests(AuthSchemaFixture db)
{
    private static readonly DateOnly SoDt = new(2026, 11, 5);
    private static readonly CancellationToken None = CancellationToken.None;

    private SalesOrderService Service => new(
        db.Get<ISalesOrderRepository>(), db.Get<ICustomerRepository>(), db.Get<ITaxRepository>(), new TaxAreaRateProvider(db.Get<ITaxRepository>()),
        db.Get<ILocalizationRepository>(), db.Get<IBusinessPlaceRepository>(),
        new ExchangeRateService(
            db.Get<IExchangeRateRepository>(), db.Get<ICurrencyRepository>(), db.Get<ILocalizationRepository>(), new FixedClock(),
            db.Get<IUnitOfWorkFactory>()),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()), new CodeValidator(db.Get<ICodeRepository>(), db.Get<IUnitOfWorkFactory>()),
        db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "SD-UC-002-T01"), Trait("Id", "SD-UT-006"), Trait("Id", "SD-UT-405"), Trait("Id", "SD-UT-401")]
    public async Task Create_NumbersTheOrder_FillsCustomerDefaults_AndServerAmounts()
    {
        var m = await MastersAsync();

        var so = await Service.CreateAsync(Request(m, Line(m.Item), Line(m.Service, qty: 2m, price: null, taxCode: m.NoTax)), "kim", None);

        so.Status.Should().Be("SO");
        SoNumber().IsMatch(so.SoNo).Should().BeTrue(so.SoNo);
        (so.Rep, so.Terms, so.DueDt, so.ShipName, so.ShipZipCode, so.FreightCharge, so.BillingFlow, so.CurrencyCode, so.DcStatus)
            .Should().Be(("DEMO_A", "NET_30", SoDt.AddDays(30), "Ship " + m.Key, "75001", "N", "SHIP_THEN_BILL", "USD", m.Discount));
        so.Lines.Select(l => (l.Seq, l.SoPrc, l.DcCd, l.DcRate, l.SoAmt, l.TaxAmt, l.StdPrc))
            .Should().Equal((1, 100m, m.Discount, -10m, 900m, 90m, 100m), (2, 5m, m.Discount, -10m, 9m, 0m, 5m));
        (so.SupplyAmt, so.TaxAmt, so.TotSoAmt, so.TotBaseAmt, so.BillingFlowLocked).Should().Be((909m, 90m, 999m, 999m, false));
        (await Service.SearchAsync(new(so.SoNo[..6], m.Customer, "SO", SoDt, SoDt), None))
            .Should().ContainSingle(s => s.Id == so.Id && s.CustomerName == "Customer " + m.Key);
    }

    [DatabaseFact, Trait("Id", "SD-UC-002-T01")]
    public async Task Search_FindsByPartOfTheCustomerName_WithoutTreatingItsWildcardsAsPatterns()
    {
        var m = await MastersAsync();
        var so = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);

        (await Service.SearchAsync(new(null, null, null, null, null, "customer " + m.Key.ToLowerInvariant()), None))
            .Should().ContainSingle(s => s.Id == so.Id);
        (await Service.SearchAsync(new(null, null, null, null, null, "%"), None)).Should().NotContain(s => s.Id == so.Id);
        (await Service.SearchAsync(new(null, null, null, null, null, "NO SUCH CUSTOMER"), None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "SD-UT-404")]
    public async Task DiscountRates_ListsTheCodesWithTheirRates_InCodeOrder()
    {
        var m = await MastersAsync();

        var rates = await Service.GetDiscountRatesAsync(None);

        rates.Should().ContainSingle(r => r.DcCd == m.Discount).Which.DcRate.Should().Be(-10m);
        rates.Select(r => r.DcCd).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [DatabaseFact, Trait("Id", "SD-UC-002-T02")]
    public async Task Update_AddsChangesAndDeletesLines()
    {
        var m = await MastersAsync();
        var so = await Service.CreateAsync(Request(m, Line(m.Item), Line(m.Service, price: 5m)), "kim", None);
        var first = so.Lines[0];

        var saved = await Service.UpdateAsync(so.Id, Saved(so,
            Line(m.Item, qty: 3m, dcCd: "") with { Id = first.Id }, Line(m.Item, price: 100m)), "lee", None);

        saved.Lines.Select(l => (l.Id == first.Id, l.Seq, l.SoQty, l.DcCd, l.SoAmt)).Should().Equal((true, 1, 3m, null, 300m), (false, 2, 10m, m.Discount, 900m));
        (saved.TotSoAmt, saved.UpdatedBy).Should().Be((1320m, "lee"));
    }

    [DatabaseFact, Trait("Id", "SD-UC-002-T03"), Trait("Id", "SD-UT-001")]
    public async Task RuleViolation_RollsBackEverything()
    {
        var m = await MastersAsync();

        var act = () => Service.CreateAsync(Request(m, Line(m.Item), Line(m.Item, qty: 0m)), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Should().Match<DomainException>(e => e.Code == "SD-E-001" && (int)e.Args[0] == 2);
        (await Service.SearchAsync(new(null, m.Customer, null, null, null), None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "SD-UC-002-T04")]
    public async Task StaleUpdatedAt_Is409()
    {
        var m = await MastersAsync();
        var so = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);
        await Service.UpdateAsync(so.Id, Saved(so, Line(m.Item, qty: 2m)), "kim", None);

        var update = () => Service.UpdateAsync(so.Id, Saved(so, Line(m.Item, qty: 3m)), "kim", None);
        var delete = () => Service.DeleteAsync(so.Id, so.UpdatedAt, None);

        await update.Should().ThrowAsync<ConcurrencyException>();
        await delete.Should().ThrowAsync<ConcurrencyException>();
    }

    [DatabaseFact, Trait("Id", "SD-UT-003"), Trait("Id", "SD-TS-002")]
    public async Task ReleasedOrder_CannotBeChangedOrDeleted_ButAnEnteredOneIsDeleted()
    {
        var m = await MastersAsync();
        var released = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);
        var entered = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);
        await ExecuteAsync("UPDATE sd_sales_order SET status = 'RE' WHERE id = @Id", new { released.Id });
        released = await Service.GetAsync(released.Id, None);

        var update = () => Service.UpdateAsync(released.Id, Saved(released, Line(m.Item)), "kim", None);
        var delete = () => Service.DeleteAsync(released.Id, released.UpdatedAt, None);
        await Service.DeleteAsync(entered.Id, entered.UpdatedAt, None);

        (await update.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("SD-E-003");
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("SD-E-003");
        await ((Func<Task>)(() => Service.GetAsync(entered.Id, None))).Should().ThrowAsync<NotFoundException>();
    }

    [DatabaseFact]
    public async Task CodeFieldNotInTheCodeMaster_IsCommonE002()
    {
        var m = await MastersAsync();

        var act = () => Service.CreateAsync(Request(m, Line(m.Item)) with { ShipVia = "CARRIER_PIGEON" }, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("Common-E-002");
    }

    [DatabaseFact, Trait("Id", "SD-UT-023")]
    public async Task AnotherCustomer_OnASavedOrder_IsSdE023()
    {
        var m = await MastersAsync();
        var other = await MastersAsync();
        var so = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);

        var act = () => Service.UpdateAsync(so.Id, Saved(so, Line(m.Item)) with { CustomerId = other.Customer }, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("SD-E-023");
    }

    [DatabaseFact, Trait("Id", "SD-UT-035"), Trait("Id", "SD-TS-015")]
    public async Task BillingFlow_ChangesUntilAShipmentExists_Then409SdE035()
    {
        var m = await MastersAsync();
        var so = await Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);
        so = await Service.UpdateAsync(so.Id, Saved(so, Keep(so)) with { BillingFlow = "BILL_THEN_SHIP" }, "kim", None);
        await ExecuteAsync(
            """
            INSERT INTO sd_shipment (ship_no, customer_id, sales_order_id, ship_dt, warehouse_id, currency_code, exchange_rate, business_place_id,
                                     created_by, updated_by)
            VALUES ('SH' || @Id, @Customer, @Id, @SoDt, (SELECT min(id) FROM bc_warehouse), 'USD', 1,
                    (SELECT id FROM bc_business_place WHERE is_default), 't', 't')
            """,
            new { so.Id, m.Customer, SoDt });
        so = await Service.GetAsync(so.Id, None);

        var change = () => Service.UpdateAsync(so.Id, Saved(so, Keep(so)) with { BillingFlow = "SHIP_AND_BILL" }, "kim", None);

        so.BillingFlowLocked.Should().BeTrue();
        (await change.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("SD-E-035");
        var same = await Service.UpdateAsync(so.Id, Saved(so, Keep(so)) with { Remarks = "ok" }, "kim", None);
        (same.BillingFlow, same.Remarks).Should().Be(("BILL_THEN_SHIP", "ok"));
    }

    [DatabaseFact, Trait("Id", "SD-UT-009")]
    public async Task ShipToDifferent_AsksFirst_ThenYesUpdatesTheCustomer()
    {
        var m = await MastersAsync();
        var request = Request(m, Line(m.Item)) with
        {
            ShipName = "Dock 2",
            ShipAddr1 = "9 Pier Rd",
            ShipCity = "Austin",
            ShipState = "TX",
            ShipZipCode = "73301",
            ShipCountry = "USA",
        };

        var unanswered = () => Service.CreateAsync(request, "kim", None);
        var warning = (await unanswered.Should().ThrowAsync<ConfirmationRequiredException>()).Which;
        var no = await Service.CreateAsync(request with { UpdateCustomerShipTo = false }, "kim", None);
        var customerAfterNo = await CustomerShipToAsync(m.Customer);
        await Service.CreateAsync(request with { UpdateCustomerShipTo = true }, "lee", None);

        (warning.Code, warning.ConfirmField).Should().Be(("SD-W-009", "updateCustomerShipTo"));
        (no.ShipName, customerAfterNo.ShipName).Should().Be(("Dock 2", "Ship " + m.Key));
        (await CustomerShipToAsync(m.Customer)).Should().Be(("Dock 2", "9 Pier Rd", "Austin", "73301", "N", "lee"));
    }

    [DatabaseFact, Trait("Id", "SD-UT-010")]
    public async Task PriceDifferentFromStandardAndLevel_AsksFirst_ThenYesSavesTheLevelPrice()
    {
        var m = await MastersAsync();

        var unanswered = () => Service.CreateAsync(Request(m, Line(m.Item, price: 120m)), "kim", None);
        var warning = (await unanswered.Should().ThrowAsync<ConfirmationRequiredException>()).Which;
        var level = await Service.CreateAsync(Request(m, Line(m.Item, price: 95m)), "kim", None);
        await Service.CreateAsync(Request(m, Line(m.Item, price: 120m)) with { UpdatePriceLevel = true }, "lee", None);

        (warning.Code, warning.ConfirmField, warning.Args.Single()).Should().Be(("SD-W-010", "updatePriceLevel", m.ItemCd));
        level.Lines[0].SoPrc.Should().Be(95m);
        (await QueryAsync<(decimal, string)>(
                "SELECT sales_prc, updated_by FROM bc_sales_price WHERE price_level = @Level AND item_id = @Item", new { m.Level, m.Item }))
            .Should().Equal((120m, "lee"));
    }

    [DatabaseFact, Trait("Id", "SD-UT-309"), Trait("Id", "SD-TS-013")]
    public async Task CreditLimit_Exceeded_AsksFirst_ThenConfirmSaves()
    {
        var m = await MastersAsync();
        await ExecuteAsync("UPDATE bc_customer SET credit_limit = 500 WHERE id = @Customer", new { m.Customer });

        var unanswered = () => Service.CreateAsync(Request(m, Line(m.Item)), "kim", None);
        var warning = (await unanswered.Should().ThrowAsync<ConfirmationRequiredException>()).Which;
        var so = await Service.CreateAsync(Request(m, Line(m.Item)) with { ConfirmCreditLimit = true }, "kim", None);

        (warning.Code, warning.ConfirmField, warning.Args[0], warning.Args[1]).Should().Be(("SD-W-309", "confirmCreditLimit", (object)500m, (object)990m));
        so.TotSoAmt.Should().Be(990m);
    }

    [DatabaseFact, Trait("Id", "SD-UT-016"), Trait("Id", "SD-UT-401"), Trait("Id", "SD-UT-405"), Trait("Id", "BC-UT-525")]
    public async Task LineDefaults_QtyOneLevelPriceDiscount_AndExemptCodeForACertificateState()
    {
        var m = await MastersAsync();
        await ExecuteAsync(
            """
            INSERT INTO sd_customer_tax_exemption (customer_id, certificate_no, state_code, valid_to, created_by, updated_by)
            VALUES (@Customer, 'EX-' || @Customer, 'CA', '2026-12-31', 't', 't');
            INSERT INTO bc_tax_code (tax_code, tax_kind, rate, created_by, updated_by) VALUES ('E' || @Customer, 'SALES_TAX_EXEMPT', 0, 't', 't');
            """,
            new { m.Customer });

        var texas = await Service.GetLineDefaultsAsync(new(m.Customer, m.ItemCd.ToLowerInvariant(), null, SoDt), None);
        var california = await Service.GetLineDefaultsAsync(new(m.Customer, m.ItemCd, "CA", SoDt), None);
        var unknown = () => Service.GetLineDefaultsAsync(new(m.Customer, "NOPE" + m.Key, null, SoDt), None);

        (texas.ItemId, texas.SoQty, texas.SoPrc, texas.StdPrc, texas.DcCd, texas.DcRate, texas.TaxCodeId)
            .Should().Be((m.Item, 1m, 95m, 100m, m.Discount, -10m, m.Vat10));
        (await QueryAsync<string>("SELECT tax_kind FROM bc_tax_code WHERE id = @Id", new { Id = california.TaxCodeId })).Should().Equal("SALES_TAX_EXEMPT");
        await unknown.Should().ThrowAsync<NotFoundException>();
    }

    [DatabaseFact, Trait("Id", "SD-UT-019"), Trait("Id", "SD-UT-020"), Trait("Id", "SD-UT-021"), Trait("Id", "SD-UT-022")]
    public async Task LineFile_AllRowsOrNone_WithCustomerPricesAndBarcodes()
    {
        var m = await MastersAsync();
        await ExecuteAsync("UPDATE bc_item SET upc_code = 'U' || @Key WHERE id = @Item; UPDATE bc_item SET valid_flag = 'N' WHERE id = @Service",
            new { m.Key, m.Item, m.Service });

        var items = await ReadAsync(m, SalesOrderLineFileKind.Item, $"{m.ItemCd},3\n\n{m.ItemCd},.\n");
        var barcodes = await ReadAsync(m, SalesOrderLineFileKind.Barcode, $"U{m.Key},2\n");
        var bad = await ReadAsync(m, SalesOrderLineFileKind.Item, $"{m.ItemCd},1\nNOPE{m.Key},1\nS{m.Key},1\n{m.ItemCd},-1\n");

        items.Lines.Select(l => (l.ItemId, l.SoQty, l.SoPrc)).Should().Equal((m.Item, 3m, 95m));
        barcodes.Lines.Select(l => (l.ItemId, l.SoQty)).Should().Equal((m.Item, 2m));
        bad.Lines.Should().BeEmpty();
        bad.Errors.Select(e => (e.LineNo, e.Reason)).Should().Equal((2, "NOT_FOUND"), (3, "NOT_IN_USE"), (4, "QTY"));
        (await Service.SearchAsync(new(null, m.Customer, null, null, null), None)).Should().BeEmpty();
    }

    private Task<SalesOrderLineFileResult> ReadAsync(Masters m, string kind, string text) =>
        Service.ReadLineFileAsync(new(m.Customer, kind, null, SoDt), text, Encoding.UTF8.GetByteCount(text), None);

    private static SaveSalesOrderRequest Request(Masters m, params SaveSalesOrderLineRequest[] lines) => Request(m, null, lines);

    // Ship-to, rep, terms, currency and billing flow are left empty: the customer gives them
    private static SaveSalesOrderRequest Request(Masters m, DateTimeOffset? updatedAt, params SaveSalesOrderLineRequest[] lines) =>
        new(m.Customer, SoDt, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
            lines, updatedAt);

    // A PUT sends the whole header as the screen shows it
    private static SaveSalesOrderRequest Saved(SalesOrderDto so, params SaveSalesOrderLineRequest[] lines) =>
        new(so.CustomerId, so.SoDt, so.ExpShipDt, so.DueDt, so.CurrencyCode, so.ExchangeRate, so.BillingFlow, so.Rep, so.Terms, so.ShipVia,
            so.FreightCharge, so.ShipName, so.ShipAddr1, so.ShipAddr2, so.ShipCity, so.ShipState, so.ShipZipCode, so.ShipCountry, so.ShipPhone,
            so.ShipFax, so.Remarks, lines, so.UpdatedAt);

    private static SaveSalesOrderLineRequest Line(long item, decimal qty = 10m, decimal? price = 100m, string? dcCd = null, long? taxCode = null) =>
        new(null, item, null, qty, price, dcCd, taxCode);

    private static SaveSalesOrderLineRequest[] Keep(SalesOrderDto so) =>
        [.. so.Lines.Select(l => new SaveSalesOrderLineRequest(l.Id, l.ItemId, l.ItemName, l.SoQty, l.SoPrc, l.DcCd ?? "", l.TaxCodeId))];

    private sealed record Masters(
        string Key, long Customer, long Item, string ItemCd, long Service, long Vat10, long NoTax, string Discount, string Level);

    private async Task<(string? ShipName, string? ShipAddr1, string? ShipCity, string? ShipZipCode, string? FreightCharge, string UpdatedBy)>
        CustomerShipToAsync(long customer) =>
        (await QueryAsync<(string?, string?, string?, string?, string?, string)>(
            "SELECT ship_name, ship_addr1, ship_city, ship_zip_code, freight_charge, updated_by FROM bc_customer WHERE id = @Customer",
            new { Customer = customer })).Single();

    // The DEMO_A sales rep code, and a customer with a 10% VAT default code, a price level with a price for the item, a discount status that is a discount code
    // (-10%), the SHIP_THEN_BILL billing flow and a Texas ship-to; an inventory item (standard 100) and a service item (standard 5)
    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Vat10, long NoTax, long Customer, long Item, long Service)>(
            """
            INSERT INTO sd_discount_rate (discount_code, rate, created_by, updated_by) VALUES ('D' || @Key, -10, 't', 't');
            INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
            SELECT m.id, 'DEMO_A', 1, 't', 't' FROM sys_code_major m
            WHERE m.major_code = 'SALES_REP' AND NOT EXISTS (SELECT 1 FROM sys_code_minor n WHERE n.major_id = m.id AND n.minor_code = 'DEMO_A');
            WITH t AS (INSERT INTO bc_tax_code (tax_code, tax_kind, rate, created_by, updated_by)
                       VALUES ('V' || @Key, 'VAT_STANDARD', 10, 't', 't') RETURNING id),
                 c AS (INSERT INTO bc_customer (c_name, rep, terms, price_level, dc_status, billing_flow, currency_code, tax_code_id,
                                                ship_name, ship_addr1, ship_city, ship_state, ship_zip_code, ship_country, freight_charge,
                                                created_by, updated_by)
                       SELECT 'Customer ' || @Key, 'DEMO_A', 'NET_30', 'L' || @Key, 'D' || @Key, 'SHIP_THEN_BILL', 'USD', id,
                              'Ship ' || @Key, '1 Main St', 'Dallas', 'TX', '75001', 'USA', 'N', 't', 't' FROM t RETURNING id),
                 i AS (INSERT INTO bc_item (item_cd, item_name, type, so_usd_prc, created_by, updated_by)
                       VALUES ('I' || @Key, 'Bolt', 'INVENTORY', 100, 't', 't') RETURNING id),
                 s AS (INSERT INTO bc_item (item_cd, item_name, type, so_usd_prc, created_by, updated_by)
                       VALUES ('S' || @Key, 'Freight', 'SERVICE', 5, 't', 't') RETURNING id),
                 p AS (INSERT INTO bc_sales_price (price_level, item_id, sales_prc, created_by, updated_by)
                       SELECT 'L' || @Key, id, 95, 't', 't' FROM i RETURNING id)
            SELECT (SELECT id FROM t), (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), (SELECT id FROM c), (SELECT id FROM i),
                   (SELECT id FROM s)
            """,
            new { Key = key });
        return new Masters(key, ids.Customer, ids.Item, "I" + key, ids.Service, ids.Vat10, ids.NoTax, "D" + key, "L" + key);
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<List<T>> QueryAsync<T>(string sql, object parameters)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return [.. await connection.QueryAsync<T>(sql, parameters)];
    }

    [GeneratedRegex(@"^SO2611\d{6}$")]
    private static partial Regex SoNumber();

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => SoDt;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}

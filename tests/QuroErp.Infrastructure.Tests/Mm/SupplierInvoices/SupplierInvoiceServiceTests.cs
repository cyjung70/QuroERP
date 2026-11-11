using System.Text.RegularExpressions;
using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.GoodsReceipts;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.SupplierInvoices;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Contracts.Mm.SupplierInvoices;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.SupplierInvoices;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.SupplierInvoices;

/// <summary>Own schema for supplier invoice tests: the services commit, so their rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class SupplierInvoiceSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "SupplierInvoiceSchema";
}

/// <summary>
/// SupplierInvoiceService with the Dapper repositories, the goods receipt service and the inventory service on real PostgreSQL
/// (MM-UC-032). Without a company the base currency is USD. Receipts are dated <see cref="GrDt"/>, invoices
/// <see cref="InvoiceDt"/> and today is <see cref="Today"/>; each test makes its own vendor and items.
/// </summary>
[Collection(SupplierInvoiceSchemaDefinition.Name)]
public partial class SupplierInvoiceServiceTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly GrDt = new(2026, 11, 4);
    private static readonly DateOnly InvoiceDt = new(2026, 11, 20);
    private static readonly DateOnly Today = new(2026, 11, 25);

    private SupplierInvoiceService Service => new(
        db.Get<ISupplierInvoiceRepository>(), db.Get<ITaxRepository>(), new TaxAreaRateProvider(db.Get<ITaxRepository>()),
        db.Get<ILocalizationRepository>(), db.Get<IExchangeRateRepository>(), db.Get<IBusinessPlaceRepository>(), Inventory,
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()), new FixedClock(), db.Get<IUnitOfWorkFactory>());

    private GoodsReceiptService Receipts => new(
        db.Get<IGoodsReceiptRepository>(), db.Get<ITaxRepository>(), new TaxAreaRateProvider(db.Get<ITaxRepository>()),
        db.Get<ILocalizationRepository>(), db.Get<IExchangeRateRepository>(), db.Get<IBusinessPlaceRepository>(),
        db.Get<IWarehouseRepository>(), Inventory, new DocumentNumberService(db.Get<IDocumentNumberRepository>()), new FixedClock(),
        db.Get<IUnitOfWorkFactory>());

    private InventoryService Inventory => new(
        db.Get<IInventoryRepository>(), db.Get<IInventorySettingRepository>(), db.Get<ISetComponentRepository>(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    [DatabaseFact, Trait("Id", "MM-UC-032-T01"), Trait("Id", "MM-UT-751"), Trait("Id", "MM-UT-752"), Trait("Id", "MM-UT-755"),
     Trait("Id", "MM-UT-757"), Trait("Id", "MM-UT-761")]
    public async Task TwoReceipts_OnOneInvoice_CloseBothReceipts()
    {
        var m = await MastersAsync();
        var first = await ReceiveAsync(m, (m.Item, 4m), (m.Service, 1m));
        var second = await ReceiveAsync(m, (m.Item, 2m));

        var invoice = await Service.CreateAsync(
            Request(m, "inv-1", Match(first.Lines[0]), Match(first.Lines[1]), Match(second.Lines[0])), "kim", None);

        InvoiceNumber().IsMatch(invoice.InvoiceNo).Should().BeTrue(invoice.InvoiceNo);
        (invoice.Type, invoice.Status, invoice.RefNo, invoice.DueDt, invoice.CurrencyCode, invoice.ExchangeRate)
            .Should().Be((SupplierInvoiceType.Invoice, SupplierInvoiceStatus.Open, "INV-1", InvoiceDt, "USD", 1m));
        (invoice.SupplyAmt, invoice.TaxAmt, invoice.TotAmt, invoice.TotBaseAmt, invoice.ClsAmt, invoice.BalAmt)
            .Should().Be((700m, 70m, 770m, 770m, 0m, 770m));
        invoice.Lines.Select(l => (l.Seq, l.GrNo, l.GrSeq, l.Qty, l.Prc, l.PvAmt))
            .Should().Equal((1, first.GrNo, 1, 4m, 100m, 0m), (2, first.GrNo, 2, 1m, 100m, 0m), (3, second.GrNo, 1, 2m, 100m, 0m));
        (await Receipts.GetAsync(first.Id, None)).Status.Should().Be(GoodsReceiptStatus.Closed);
        (await Receipts.GetAsync(second.Id, None)).Status.Should().Be(GoodsReceiptStatus.Closed);
        (await Service.OpenReceiptLinesAsync(m.Vendor, "USD", null, None)).Should().BeEmpty();
        (await Service.SearchAsync(new(invoice.InvoiceNo[..6], m.Vendor, InvoiceDt, InvoiceDt, "INV", "OP"), None))
            .Should().ContainSingle(s => s.Id == invoice.Id && s.BalAmt == 770m);
    }

    [DatabaseFact, Trait("Id", "MM-UC-032-T02"), Trait("Id", "MM-UT-753"), Trait("Id", "MM-UT-763")]
    public async Task OneReceiptLine_SplitOverTwoInvoices_UpToItsOpenQty()
    {
        var m = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m));

        var a = await Service.CreateAsync(Request(m, "a", Match(gr.Lines[0], 4m)), "kim", None);
        var open = await Service.OpenReceiptLinesAsync(m.Vendor, null, null, None);
        var openForA = await Service.OpenReceiptLinesAsync(m.Vendor, "USD", a.Id, None);
        await Service.CreateAsync(Request(m, "b", Match(gr.Lines[0], 6m)), "kim", None);
        var over = () => Service.CreateAsync(Request(m, "c", Match(gr.Lines[0], 1m)), "kim", None);

        open.Select(l => (l.GrNo, l.GrSeq, l.OpenQty, l.GrPrc, l.PoPrc)).Should().Equal((gr.GrNo, 1, 6m, 100m, (decimal?)null));
        openForA.Should().BeEmpty("the invoice already has the line");
        (await Receipts.GetAsync(gr.Id, None)).Lines.Single().InvoicedQty.Should().Be(10m);
        (await over.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(1, 0m);
    }

    [DatabaseFact, Trait("Id", "MM-UC-032-T03"), Trait("Id", "MM-UT-754"), Trait("Id", "MM-UT-758"), Trait("Id", "MM-UT-759"),
     Trait("Id", "MM-UT-005"), Trait("Id", "MM-UT-606")]
    public async Task APriceDifference_IsAsked_ThenPostsAPriceVariance()
    {
        var m = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m));
        var request = Request(m, "pv", Match(gr.Lines[0], 4m) with { Prc = 110m });

        var ask = () => Service.CreateAsync(request, "kim", None);
        var asked = (await ask.Should().ThrowAsync<ConfirmationRequiredException>()).Which;
        var invoice = await Service.CreateAsync(request with { ConfirmPriceDifference = true }, "kim", None);

        (asked.Code, asked.ConfirmField).Should().Be((SupplierInvoiceRules.PriceDifferenceWarningCode, SupplierInvoiceRules.ConfirmPriceDifferenceField));
        asked.Args.Should().Equal(1, 110m, 100m);
        invoice.Lines.Single().PvAmt.Should().Be(40m);
        (await MovementsAsync(invoice.InvoiceNo)).Should().Equal(("PV", 1, 0m, 40m, InvoiceDt, false));
        (await StockValueAsync(m.Item)).Should().Be(1040m);
    }

    [DatabaseFact, Trait("Id", "MM-UC-032-T04"), Trait("Id", "MM-UT-759"), Trait("Id", "MM-UT-004")]
    public async Task AnInvoiceInAClosedMonth_PostsItsVarianceOnTheFirstOpenDay()
    {
        var m = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m));
        // close_year_month is the first open month: November is closed
        await CloseMonthAsync("202612");
        try
        {
            var invoice = await Service.CreateAsync(
                Request(m, "closed", Match(gr.Lines[0], 5m) with { Prc = 90m }) with { ConfirmPriceDifference = true }, "kim", None);

            (await MovementsAsync(invoice.InvoiceNo)).Should().Equal(("PV", 1, 0m, -50m, new DateOnly(2026, 12, 1), false));
        }
        finally
        {
            await CloseMonthAsync(null);
        }
    }

    [DatabaseFact, Trait("Id", "MM-UC-032-T05"), Trait("Id", "MM-UT-760")]
    public async Task APaidInvoice_CannotBeChangedOrDeleted()
    {
        var m = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m));
        var invoice = await Service.CreateAsync(Request(m, "paid", Match(gr.Lines[0])), "kim", None);
        await ExecuteAsync("UPDATE mm_supplier_invoice SET cls_amt = 100, bal_amt = tot_amt - 100, status = 'PA' WHERE id = @Id", invoice.Id);
        var paid = await Service.GetAsync(invoice.Id, None);

        var update = () => Service.UpdateAsync(invoice.Id, Request(m, paid.UpdatedAt, "paid", Keep(paid.Lines[0])), "kim", None);
        var delete = () => Service.DeleteAsync(invoice.Id, paid.UpdatedAt, "kim", None);

        (await update.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SupplierInvoiceRules.LockedCode);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SupplierInvoiceRules.LockedCode);
        paid.Status.Should().Be(SupplierInvoiceStatus.PartiallyPaid);
    }

    [DatabaseFact, Trait("Id", "MM-UC-032-T06"), Trait("Id", "MM-UT-757"), Trait("Id", "MM-UT-760"), Trait("Id", "MM-UT-601")]
    public async Task DeletingAnInvoice_GivesBackItsQty_AndReversesItsVariance()
    {
        var m = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m));
        var invoice = await Service.CreateAsync(
            Request(m, "del", Match(gr.Lines[0], 4m) with { Prc = 110m }) with { ConfirmPriceDifference = true }, "kim", None);

        await Service.DeleteAsync(invoice.Id, invoice.UpdatedAt, "kim", None);

        var receipt = await Receipts.GetAsync(gr.Id, None);
        (receipt.Status, receipt.Lines.Single().InvoicedQty).Should().Be((GoodsReceiptStatus.Open, 0m));
        (await MovementsAsync(invoice.InvoiceNo)).Should().Equal(("PV", 1, 0m, 40m, InvoiceDt, false), ("PV", 1, 0m, -40m, Today, true));
        (await StockValueAsync(m.Item)).Should().Be(1000m);
        (await CountAsync("SELECT count(*) FROM mm_supplier_invoice WHERE id = @Id", invoice.Id)).Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "MM-UT-760"), Trait("Id", "MM-UT-757")]
    public async Task Update_AddsAndDeletesLines_AndRefusesAChangedLine()
    {
        var m = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m), (m.Service, 2m));
        var invoice = await Service.CreateAsync(
            Request(m, "upd", Match(gr.Lines[0], 4m) with { Prc = 110m }, Match(gr.Lines[1], 2m)) with { ConfirmPriceDifference = true }, "kim", None);

        var changed = () => Service.UpdateAsync(
            invoice.Id, Request(m, invoice.UpdatedAt, "upd", Keep(invoice.Lines[0]) with { Qty = 5m }), "kim", None);
        (await changed.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SupplierInvoiceRules.LockedCode);
        var saved = await Service.UpdateAsync(
            invoice.Id, Request(m, invoice.UpdatedAt, "upd", Keep(invoice.Lines[1]), Match(gr.Lines[0], 10m)) with { DueDt = InvoiceDt.AddDays(30) },
            "kim", None);

        saved.Lines.Select(l => (l.Seq, l.Qty, l.PvAmt)).Should().Equal((2, 2m, 0m), (3, 10m, 0m));
        (saved.DueDt, saved.TotAmt).Should().Be((InvoiceDt.AddDays(30), 1320m));
        (await Receipts.GetAsync(gr.Id, None)).Lines.Select(l => l.InvoicedQty).Should().Equal(10m, 2m);
        (await MovementsAsync(invoice.InvoiceNo)).Should().Equal(("PV", 1, 0m, 40m, InvoiceDt, false), ("PV", 1, 0m, -40m, Today, true));
    }

    [DatabaseFact, Trait("Id", "MM-UT-760")]
    public async Task ANewDateOrRate_IsRefusedWhileLinesAreSaved()
    {
        var m = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m));
        var invoice = await Service.CreateAsync(Request(m, "date", Match(gr.Lines[0], 1m)), "kim", None);
        var keep = Request(m, invoice.UpdatedAt, "date", Keep(invoice.Lines[0]));

        var date = () => Service.UpdateAsync(invoice.Id, keep with { InvoiceDt = InvoiceDt.AddDays(1) }, "kim", None);
        var rate = () => Service.UpdateAsync(invoice.Id, keep with { ExchangeRate = 1.1m }, "kim", None);
        var replaced = () => Service.UpdateAsync(
            invoice.Id, Request(m, invoice.UpdatedAt, "date", Match(gr.Lines[0], 2m)) with { InvoiceDt = InvoiceDt.AddDays(1) }, "kim", None);

        (await date.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SupplierInvoiceRules.LockedCode);
        (await rate.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SupplierInvoiceRules.LockedCode);
        (await replaced.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SupplierInvoiceRules.LockedCode);
        (await Service.UpdateAsync(invoice.Id, keep with { DueDt = InvoiceDt.AddDays(30) }, "kim", None)).DueDt.Should().Be(InvoiceDt.AddDays(30));
    }

    [DatabaseFact, Trait("Id", "MM-UT-764")]
    public async Task VendorInvoiceNumbers_AreUniquePerVendor()
    {
        var m = await MastersAsync();
        var other = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m));
        var otherGr = await ReceiveAsync(other, (other.Item, 10m));
        await Service.CreateAsync(Request(m, "same", Match(gr.Lines[0], 1m)), "kim", None);

        var duplicate = () => Service.CreateAsync(Request(m, " SAME ", Match(gr.Lines[0], 1m)), "kim", None);
        var otherVendor = await Service.CreateAsync(Request(other, "same", Match(otherGr.Lines[0], 1m)), "kim", None);

        (await duplicate.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("SAME");
        otherVendor.RefNo.Should().Be("SAME");
    }

    [DatabaseFact, Trait("Id", "MM-UT-762"), Trait("Id", "MM-TS-022")]
    public async Task DebitMemos_CannotBeChangedOrDeletedHere()
    {
        var m = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m));
        var invoice = await Service.CreateAsync(Request(m, "dm", Match(gr.Lines[0], 1m)), "kim", None);
        await ExecuteAsync("UPDATE mm_supplier_invoice SET type = 'DM' WHERE id = @Id", invoice.Id);
        var memo = await Service.GetAsync(invoice.Id, None);

        var update = () => Service.UpdateAsync(invoice.Id, Request(m, memo.UpdatedAt, "dm", Keep(memo.Lines[0])), "kim", None);
        var delete = () => Service.DeleteAsync(invoice.Id, memo.UpdatedAt, "kim", None);

        (await update.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SupplierInvoiceRules.DebitMemoCode);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SupplierInvoiceRules.DebitMemoCode);
    }

    [DatabaseFact, Trait("Id", "MM-UT-751")]
    public async Task AReceiptLineOfAnotherVendor_IsRefused_AndTheReceiptIsLocked()
    {
        var m = await MastersAsync();
        var other = await MastersAsync();
        var otherGr = await ReceiveAsync(other, (other.Item, 10m));
        var gr = await ReceiveAsync(m, (m.Item, 10m));
        await Service.CreateAsync(Request(m, "lock", Match(gr.Lines[0], 1m)), "kim", None);

        var foreign = () => Service.CreateAsync(Request(m, "x", Match(otherGr.Lines[0], 1m)), "kim", None);
        var receiptDelete = () => Receipts.DeleteAsync(gr.Id, gr.UpdatedAt, "kim", None);

        (await foreign.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SupplierInvoiceRules.ReceiptMismatchCode);
        (await receiptDelete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(GoodsReceiptRules.LockedCode);
    }

    [DatabaseFact]
    public async Task StaleUpdatedAt_Is409()
    {
        var m = await MastersAsync();
        var gr = await ReceiveAsync(m, (m.Item, 10m));
        var invoice = await Service.CreateAsync(Request(m, "stale", Match(gr.Lines[0], 1m)), "kim", None);
        await Service.UpdateAsync(invoice.Id, Request(m, invoice.UpdatedAt, "stale", Keep(invoice.Lines[0])) with { Remarks = "x" }, "kim", None);

        var update = () => Service.UpdateAsync(invoice.Id, Request(m, invoice.UpdatedAt, "stale", Keep(invoice.Lines[0])), "kim", None);
        var delete = () => Service.DeleteAsync(invoice.Id, invoice.UpdatedAt, "kim", None);

        await update.Should().ThrowAsync<ConcurrencyException>();
        await delete.Should().ThrowAsync<ConcurrencyException>();
    }

    [DatabaseFact, Trait("Id", "BC-UT-425"), Trait("Rule", "BC-BR-425")]
    public async Task InvoiceLines_CountForTheirDocumentCurrency()
    {
        var m = await MastersAsync();
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                WITH s AS (INSERT INTO mm_supplier_invoice (invoice_no, vendor_id, business_place_id, ref_no, invoice_dt, due_dt, currency_code,
                                                            exchange_rate, created_by, updated_by)
                           SELECT 'VI-EUR-' || @Vendor, @Vendor, id, 'EUR-1', '2026-11-20', '2026-11-20', 'EUR', 1.1, 't', 't'
                           FROM bc_business_place WHERE is_default RETURNING id)
                INSERT INTO mm_supplier_invoice_line (supplier_invoice_id, seq, item_id, qty, prc, tax_code_id, created_by, updated_by)
                SELECT s.id, 1, @Item, 1, 10, @Tax, 't', 't' FROM s
                """, new { m.Vendor, m.Item, Tax = m.Vat10 });
        }

        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        (await db.Get<ILocalizationSettingRepository>().FindDocumentCurrenciesAsync(DecimalModule.Mm, uow.Transaction, None))
            .Should().Contain("EUR");
    }

    private static SaveSupplierInvoiceRequest Request(Masters m, string refNo, params SaveSupplierInvoiceLineRequest[] lines) =>
        Request(m, null, refNo, lines);

    private static SaveSupplierInvoiceRequest Request(Masters m, DateTimeOffset? updatedAt, string refNo, params SaveSupplierInvoiceLineRequest[] lines) =>
        new(m.Vendor, refNo, InvoiceDt, null, null, null, false, null, lines, updatedAt);

    // A receipt line with its open qty (or qty) and the receipt's price and tax
    private static SaveSupplierInvoiceLineRequest Match(GoodsReceiptLineDto line, decimal? qty = null) =>
        new(null, line.Id, qty ?? line.GrQty - line.InvoicedQty - line.ReturnedQty);

    private static SaveSupplierInvoiceLineRequest Keep(SupplierInvoiceLineDto l) =>
        new(l.Id, l.GoodsReceiptLineId!.Value, l.Qty, l.Prc, l.TaxCodeId, l.Deductible);

    private Task<GoodsReceiptDto> ReceiveAsync(Masters m, params (long Item, decimal Qty)[] lines) =>
        Receipts.CreateAsync(
            new SaveGoodsReceiptRequest(
                m.Vendor, GrDt, m.Main, "dn", null, null, null,
                [.. lines.Select(l => new SaveGoodsReceiptLineRequest(null, l.Item, null, null, null, null, l.Qty, 100m, null))], null),
            "kim", None);

    private sealed record Masters(long Vendor, long Item, long Service, long Main, long Vat10);

    // A USD vendor whose default tax code is a 10% VAT code, an inventory item, a service item and the default warehouse
    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync("UPDATE mm_inventory_setting SET negative_stock_mode = 'DENY', over_receipt_mode = 'DENY'");
        var ids = await connection.QuerySingleAsync<(long Vat10, long Vendor, long Item, long Service, long Main)>(
            """
            WITH t AS (INSERT INTO bc_tax_code (tax_code, tax_kind, rate, created_by, updated_by)
                       VALUES ('V' || @Key, 'VAT_STANDARD', 10, 't', 't') RETURNING id),
                 v AS (INSERT INTO bc_vendor (v_name, currency_code, tax_code_id, created_by, updated_by)
                       SELECT 'Vendor ' || @Key, 'USD', id, 't', 't' FROM t RETURNING id),
                 i AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('I' || @Key, 'Bolt', 'INVENTORY', 't', 't') RETURNING id),
                 s AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('S' || @Key, 'Freight', 'SERVICE', 't', 't') RETURNING id)
            SELECT (SELECT id FROM t), (SELECT id FROM v), (SELECT id FROM i), (SELECT id FROM s),
                   (SELECT id FROM bc_warehouse WHERE is_default)
            """,
            new { Key = key });
        return new Masters(ids.Vendor, ids.Item, ids.Service, ids.Main, ids.Vat10);
    }

    // The test schema has no company row until one is set up; a US company keeps the USD base currency of the other tests
    private async Task CloseMonthAsync(string? closeYearMonth)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
            SELECT 'Quro', 'US', 'USD', 'en', 'UTC', 't', 't' WHERE NOT EXISTS (SELECT 1 FROM bc_company);
            UPDATE bc_company SET close_year_month = @Close;
            """, new { Close = closeYearMonth });
    }

    // PV movements of an invoice in id order: type, line, qty, amount, document date and whether it is a reversal
    private async Task<List<(string MovType, int RefSeq, decimal Qty, decimal Amount, DateOnly DocumentDt, bool Reversal)>> MovementsAsync(string invoiceNo)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return [.. (await connection.QueryAsync<(string, int, decimal, decimal, DateTime, bool)>(
                """
                SELECT mov_type, ref_seq, qty, amount, document_dt, parent_movement_id IS NOT NULL
                FROM mm_goods_movement WHERE ref_key = @No ORDER BY id
                """, new { No = invoiceNo }))
            .Select(r => (r.Item1, r.Item2, r.Item3, r.Item4, DateOnly.FromDateTime(r.Item5), r.Item6))];
    }

    // The item's stock value over every warehouse
    private async Task<decimal> StockValueAsync(long item)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<decimal>(
            "SELECT coalesce(sum(amount), 0) FROM mm_goods_movement WHERE item_id = @Item", new { Item = item });
    }

    private async Task<int> CountAsync(string sql, long id)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(sql, new { Id = id });
    }

    private async Task ExecuteAsync(string sql, long id)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => SupplierInvoiceServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }

    [GeneratedRegex(@"^VI2611\d{6}$")]
    private static partial Regex InvoiceNumber();
}

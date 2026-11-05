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
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Application.Sd.Customers;
using QuroErp.Application.Sd.SalesInvoices;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Application.Sd.Shipments;
using QuroErp.Contracts.Sd.SalesInvoices;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.SalesInvoices;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Sd.SalesInvoices;

/// <summary>Own schema for sales invoice tests: the services commit, so their rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class SalesInvoiceSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "SalesInvoiceSchema";
}

/// <summary>
/// SalesInvoiceService with the Dapper repositories and the inventory service on real PostgreSQL (SD-UC-003). Without a company
/// the base currency is USD. Today is <see cref="Today"/>. Each test makes its own customer, items and orders and receives the
/// stock it bills at a cost of 50; the negative stock mode is DENY.
/// </summary>
[Collection(SalesInvoiceSchemaDefinition.Name)]
public partial class SalesInvoiceServiceTests(AuthSchemaFixture db)
{
    private const string ShipAndBill = "SHIP_AND_BILL";
    private const string ShipThenBill = "SHIP_THEN_BILL";
    private const string BillThenShip = "BILL_THEN_SHIP";
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly DnDt = new(2026, 11, 4);
    private static readonly DateOnly Today = new(2026, 11, 10);

    private SalesInvoiceService Service => new(
        db.Get<ISalesInvoiceRepository>(), db.Get<IShipmentRepository>(), db.Get<ISalesOrderRepository>(), db.Get<ICustomerRepository>(),
        db.Get<ITaxRepository>(), new TaxAreaRateProvider(db.Get<ITaxRepository>()), db.Get<ILocalizationRepository>(),
        db.Get<IBusinessPlaceRepository>(), db.Get<IWarehouseRepository>(), Rates, Inventory,
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()), new CodeValidator(db.Get<ICodeRepository>(), db.Get<IUnitOfWorkFactory>()),
        new FixedClock(), db.Get<IUnitOfWorkFactory>());

    private ShipmentService Shipments => new(
        db.Get<IShipmentRepository>(), db.Get<ICustomerRepository>(), db.Get<ITaxRepository>(), new TaxAreaRateProvider(db.Get<ITaxRepository>()),
        db.Get<ILocalizationRepository>(), db.Get<IBusinessPlaceRepository>(), db.Get<IWarehouseRepository>(), Rates, Inventory,
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()), new CodeValidator(db.Get<ICodeRepository>(), db.Get<IUnitOfWorkFactory>()),
        new FixedClock(), db.Get<IUnitOfWorkFactory>());

    private ExchangeRateService Rates => new(
        db.Get<IExchangeRateRepository>(), db.Get<ICurrencyRepository>(), db.Get<ILocalizationRepository>(), new FixedClock(),
        db.Get<IUnitOfWorkFactory>());

    private InventoryService Inventory => new(
        db.Get<IInventoryRepository>(), db.Get<IInventorySettingRepository>(), db.Get<ISetComponentRepository>(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    [DatabaseFact, Trait("Id", "SD-UC-003-T01"), Trait("Id", "SD-UT-106"), Trait("Id", "SD-UT-108"), Trait("Id", "SD-UT-109"),
     Trait("Id", "SD-UT-110"), Trait("Id", "SD-UT-115"), Trait("Id", "SD-UT-116")]
    public async Task Create_BillsAShipmentLineAndADirectLine_WithANewNumber()
    {
        var m = await MastersAsync(ShipThenBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        var sh = await Shipments.CreateAsync(ShipmentRequest(m, ShipmentLine(m.Item, 3m)), "kim", None);

        var inv = await Service.CreateAsync(Request(m, FromShipment(sh.Lines[0].Id, 2m), Direct(m.Service, 1m, 30m, m.NoTax)), "kim", None);

        InvoiceNumber().IsMatch(inv.DnNo).Should().BeTrue(inv.DnNo);
        (inv.Status, inv.BillingFlow, inv.DnDt, inv.CurrencyCode, inv.WarehouseId, inv.Rep).Should().Be(("OP", ShipThenBill, DnDt, "USD", m.Main, "DEMO_A"));
        inv.Lines.Select(l => (l.Seq, l.ItemId, l.DnQty, l.DnPrc, l.TaxCodeId, l.ShipmentLineId, l.ShipNo, l.UnshippedQty)).Should().Equal(
            (1, m.Item, 2m, 100m, m.Vat10, (long?)sh.Lines[0].Id, sh.ShipNo, 0m), (2, m.Service, 1m, 30m, m.NoTax, null, null, 0m));
        (inv.SupplyAmt, inv.TaxAmt, inv.TotDnAmt, inv.BalAmt).Should().Be((230m, 20m, 250m, 250m));
        (await ShipmentLineAsync(sh.Lines[0].Id)).Should().Be((2m, "PB"));
        (await Service.GetAsync(inv.Id, None)).Should().BeEquivalentTo(inv);
        (await Service.SearchAsync(new(inv.DnNo, m.Customer, null, DnDt, DnDt), None))
            .Should().ContainSingle(s => s.Id == inv.Id && s.TotDnAmt == 250m && s.BillingFlow == ShipThenBill);
    }

    [DatabaseFact, Trait("Id", "SD-UC-003-T02"), Trait("Id", "SD-UT-107"), Trait("Id", "SD-UT-108"), Trait("Id", "SD-UT-110"), Trait("Id", "SD-UT-102")]
    public async Task Update_AddsAndDeletesLines_AndDeleteGivesTheBilledQtyBack()
    {
        var m = await MastersAsync(ShipThenBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        var sh = await Shipments.CreateAsync(ShipmentRequest(m, ShipmentLine(m.Item, 3m)), "kim", None);
        var inv = await Service.CreateAsync(Request(m, FromShipment(sh.Lines[0].Id, 2m), Direct(m.Service, 1m, 30m, m.NoTax)), "kim", None);

        var over = () => Service.UpdateAsync(inv.Id, Saved(inv, Keep(inv.Lines[0]), FromShipment(sh.Lines[0].Id, 2m)), "kim", None);
        var change = () => Service.UpdateAsync(inv.Id, Saved(inv, Keep(inv.Lines[0]) with { DnQty = 1m }), "kim", None);
        (await over.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(sh.ShipNo, 1, 1m);
        (await change.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesInvoiceRules.LineChangedCode);

        // The deleted line gives its qty back first, so the whole shipment line can be billed again
        var saved = await Service.UpdateAsync(inv.Id, Saved(inv, Keep(inv.Lines[1]), FromShipment(sh.Lines[0].Id, 3m)), "kim", None);

        saved.Lines.Select(l => (l.Seq, l.DnQty)).Should().Equal((2, 1m), (3, 3m));
        (saved.SupplyAmt, saved.TaxAmt, saved.TotDnAmt).Should().Be((330m, 30m, 360m));
        (await ShipmentLineAsync(sh.Lines[0].Id)).Should().Be((3m, "BL"));

        await Service.DeleteAsync(inv.Id, saved.UpdatedAt, "kim", None);
        (await ShipmentLineAsync(sh.Lines[0].Id)).Should().Be((0m, "OP"));
        var get = () => Service.GetAsync(inv.Id, None);
        await get.Should().ThrowAsync<NotFoundException>();
    }

    [DatabaseFact, Trait("Id", "SD-UC-003-T03")]
    public async Task AFailedSave_RollsEverythingBack()
    {
        var m = await MastersAsync(BillThenShip);
        await ReceiveAsync(m.Item, m.Main, 2m);
        var so = await OrderAsync(m, BillThenShip, 5m, 100m);

        // The header and lines are written before the consignment move finds the stock short (DENY)
        var act = () => Service.CreateAsync(Request(m, FromOrder(so.Line, 5m)), "kim", None);

        await act.Should().ThrowAsync<DomainException>();
        (await CountAsync("SELECT count(*) FROM sd_sales_invoice WHERE customer_id = @Id", m.Customer)).Should().Be(0);
        (await OrderLineDnQtyAsync(so.Line)).Should().Be(0m);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(2m);
        (await OnHandAsync(m.Item, m.Consignment)).Should().Be(0m);
    }

    [DatabaseFact, Trait("Id", "SD-UC-003-T04"), Trait("Id", "SD-UT-119")]
    public async Task StaleUpdatedAt_Is409_AnotherCustomerIsRefused_AndAVoidInvoiceIsStale()
    {
        var m = await MastersAsync(ShipThenBill);
        var other = await MastersAsync(ShipThenBill);
        var inv = await Service.CreateAsync(Request(m, Direct(m.Service, 1m, 30m, m.NoTax)), "kim", None);
        var saved = await Service.UpdateAsync(inv.Id, Saved(inv, Keep(inv.Lines[0])) with { Remarks = "x" }, "kim", None);

        var stale = () => Service.UpdateAsync(inv.Id, Saved(inv, Keep(inv.Lines[0])), "kim", None);
        var customer = () => Service.UpdateAsync(inv.Id, Saved(saved, Keep(saved.Lines[0])) with { CustomerId = other.Customer }, "kim", None);

        saved.Remarks.Should().Be("x");
        await stale.Should().ThrowAsync<ConcurrencyException>();
        (await customer.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesInvoiceRules.CustomerLockedCode);

        var voided = await Service.VoidAsync(inv.Id, saved.UpdatedAt, "kim", None);
        var afterVoid = () => Service.UpdateAsync(inv.Id, Saved(voided, Keep(voided.Lines[0])), "kim", None);
        (voided.Status, voided.TotDnAmt, voided.BalAmt).Should().Be(("VO", 0m, 0m));
        await afterVoid.Should().ThrowAsync<ConcurrencyException>();
    }

    [DatabaseFact, Trait("Id", "SD-UT-141"), Trait("Id", "SD-UT-142"), Trait("Id", "SD-UT-143"), Trait("Id", "MM-UT-709"), Trait("Id", "MM-UT-710")]
    public async Task BillFirst_MovesTheStockToConsignment_KeepsItsValue_AndSellableStockLeavesConsignmentOut()
    {
        var m = await MastersAsync(BillThenShip);
        await ReceiveAsync(m.Item, m.Main, 6m);
        await ReceiveAsync(m.Item, m.Consignment, 2m);
        var so = await OrderAsync(m, BillThenShip, 6m, 100m);
        var valueBefore = await StockValueAsync(m.Item);

        // The consignment warehouse's 2 are not sellable
        (await Service.OpenOrderLinesAsync(m.Customer, "usd", None)).Select(l => (l.SoNo, l.RemainingQty, l.SellableQty))
            .Should().Equal((so.SoNo, 6m, 6m));

        var inv = await Service.CreateAsync(Request(m, FromOrder(so.Line, 6m)), "kim", None);

        (inv.BillingFlow, inv.SalesOrderId, inv.SoNo).Should().Be((BillThenShip, (long?)so.Id, so.SoNo));
        inv.Lines.Select(l => (l.WarehouseId, l.DnQty, l.DnPrc, l.SalesOrderLineId, l.UnshippedQty)).Should().Equal((m.Main, 6m, 100m, (long?)so.Line, 6m));
        (await OnHandAsync(m.Item, m.Main)).Should().Be(0m);
        (await OnHandAsync(m.Item, m.Consignment)).Should().Be(8m);
        (await MovementsAsync(inv.DnNo)).Should().Equal(("TR", 1, -6m, -300m, (DateOnly?)null), ("TR", 1, 6m, 300m, null));
        (await StockValueAsync(m.Item)).Should().Be(valueBefore);
        (await OrderLineDnQtyAsync(so.Line)).Should().Be(6m);
        (await Service.OpenOrderLinesAsync(m.Customer, "USD", None)).Should().BeEmpty();

        var over = () => Service.CreateAsync(Request(m, FromOrder(so.Line, 1m)), "kim", None);
        (await over.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(so.SoNo, 1, 0m);

        // SD-BR-144: nothing shipped yet, so the void takes the move and the billed qty back
        await Service.VoidAsync(inv.Id, inv.UpdatedAt, "kim", None);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(6m);
        (await OnHandAsync(m.Item, m.Consignment)).Should().Be(2m);
        (await OrderLineDnQtyAsync(so.Line)).Should().Be(0m);
        (await StockValueAsync(m.Item)).Should().Be(valueBefore);
    }

    [DatabaseFact, Trait("Id", "SD-UT-144"), Trait("Id", "SD-UT-145"), Trait("Id", "SD-UT-146"), Trait("Id", "SD-UT-206"), Trait("Id", "SD-UT-147")]
    public async Task AfterAPartialShipment_TheUnshippedQtyIsCancelledWithACreditMemo_AndTheCancellationCanBeUndone()
    {
        var m = await MastersAsync(BillThenShip);
        await ReceiveAsync(m.Item, m.Main, 6m);
        var so = await OrderAsync(m, BillThenShip, 6m, 100m);
        var inv = await Service.CreateAsync(Request(m, FromOrder(so.Line, 6m)), "kim", None);
        await Shipments.CreateAsync(ShipmentRequest(m, ShipmentLine(m.Item, 4m) with { SalesInvoiceLineId = inv.Lines[0].Id }), "kim", None);
        inv = await Service.GetAsync(inv.Id, None);
        inv.Lines[0].Should().Match<SalesInvoiceLineDto>(l => l.ShippedQty == 4m && l.UnshippedQty == 2m);

        var voidIt = () => Service.VoidAsync(inv.Id, inv.UpdatedAt, "kim", None);
        var deleteLine = () => Service.UpdateAsync(inv.Id, Saved(inv), "kim", None);
        (await voidIt.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(inv.DnNo, 1);
        (await deleteLine.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesInvoiceRules.ShippedCode);

        var cm = await Service.CancelUnshippedAsync(
            inv.Id, new CancelUnshippedRequest(DnDt, [true], [new(inv.Lines[0].Id, 2m)], inv.UpdatedAt), "kim", None);

        CreditMemoNumber().IsMatch(cm.CmNo).Should().BeTrue(cm.CmNo);
        (await OnHandAsync(m.Item, m.Consignment)).Should().Be(0m);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(2m);
        (await MovementsAsync(cm.CmNo)).Should().Equal(("TR", 1, -2m, -100m, (DateOnly?)null), ("TR", 1, 2m, 100m, null));
        var memo = await CreditMemoAsync(cm.CreditMemoId);
        (memo.Type, memo.Status, memo.TotDnAmt, memo.BalAmt).Should().Be(("CM", "OP", -200m, -200m));
        memo.Remarks.Should().Contain(inv.DnNo);
        (await OrderLineDnQtyAsync(so.Line)).Should().Be(4m);
        inv = await Service.GetAsync(inv.Id, None);
        inv.Lines[0].Should().Match<SalesInvoiceLineDto>(l => l.CancelledQty == 2m && l.UnshippedQty == 0m);
        inv.Cancellations.Should().Equal(new SalesInvoiceCancellationDto(cm.CreditMemoId, cm.CmNo, DnDt, -200m, -200m, false));
        var creditMemo = () => Service.GetAsync(cm.CreditMemoId, None);
        await creditMemo.Should().ThrowAsync<NotFoundException>("a Credit Memo is not an invoice (SD-BR-120)");

        var again = () => Service.CancelUnshippedAsync(inv.Id, new CancelUnshippedRequest(DnDt, null, [new(inv.Lines[0].Id, 1m)], inv.UpdatedAt), "kim", None);
        (await again.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(inv.DnNo, 1, 0m);

        // SD-BR-146: an applied Credit Memo keeps the cancellation
        await ExecuteAsync("UPDATE sd_sales_invoice SET bal_amt = -100 WHERE id = @Id", cm.CreditMemoId);
        var applied = () => Service.UndoCancellationAsync(inv.Id, cm.CreditMemoId, null, "kim", None);
        (await applied.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesInvoiceRules.CreditAppliedCode);
        await ExecuteAsync("UPDATE sd_sales_invoice SET bal_amt = tot_dn_amt WHERE id = @Id", cm.CreditMemoId);

        await Service.UndoCancellationAsync(inv.Id, cm.CreditMemoId, inv.UpdatedAt, "kim", None);

        (await OnHandAsync(m.Item, m.Consignment)).Should().Be(2m);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(0m);
        (await OrderLineDnQtyAsync(so.Line)).Should().Be(6m);
        (await CountAsync("SELECT count(*) FROM sd_sales_invoice WHERE id = @Id", cm.CreditMemoId)).Should().Be(0);
        inv = await Service.GetAsync(inv.Id, None);
        (inv.Lines[0].CancelledQty, inv.Lines[0].UnshippedQty, inv.Cancellations.Count).Should().Be((0m, 2m, 0));
    }

    [DatabaseFact, Trait("Id", "SD-UT-113"), Trait("Id", "SD-UT-136"), Trait("Id", "SD-UT-104"), Trait("Id", "SD-UT-116")]
    public async Task InvoiceOfAShipAndBillShipment_TakesDirectServiceLines_AndIsVoidedOnTheShipment()
    {
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 10m);
        var sh = await Shipments.CreateAsync(ShipmentRequest(m, ShipmentLine(m.Item, 2m)), "kim", None);
        var inv = await Service.GetAsync(sh.SalesInvoiceId!.Value, None);
        inv.Rep.Should().BeNull("a shipment without an order has no rep");
        inv = inv with { Rep = "DEMO_A" }; // the screen asks for the required rep (sales-invoice.md "rep")

        var voidIt = () => Service.VoidAsync(inv.Id, inv.UpdatedAt, "kim", None);
        var delete = () => Service.DeleteAsync(inv.Id, inv.UpdatedAt, "kim", None);
        var dropShipmentLine = () => Service.UpdateAsync(inv.Id, Saved(inv, Direct(m.Service, 1m, 30m, m.NoTax)), "kim", None);
        var stockItem = () => Service.UpdateAsync(inv.Id, Saved(inv, Keep(inv.Lines[0]), Direct(m.Item, 1m, 30m, m.NoTax)), "kim", None);
        (inv.ShipmentId, inv.ShipNo, inv.BillingFlow).Should().Be(((long?)sh.Id, sh.ShipNo, ShipAndBill));
        (await voidIt.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesInvoiceRules.MadeByShipmentCode);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesInvoiceRules.MadeByShipmentCode);
        (await dropShipmentLine.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesInvoiceRules.ShipmentLineCode);
        (await stockItem.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesInvoiceRules.StockItemCode);

        var saved = await Service.UpdateAsync(inv.Id, Saved(inv, Keep(inv.Lines[0]), Direct(m.Service, 1m, 30m, m.NoTax)), "kim", None);

        saved.Lines.Select(l => (l.Seq, l.ShipmentLineId)).Should().Equal((1, (long?)sh.Lines[0].Id), (2, null));
        (saved.SupplyAmt, saved.TaxAmt, saved.TotDnAmt, saved.BillingFlow).Should().Be((230m, 20m, 250m, ShipAndBill));

        await ExecuteAsync("UPDATE sd_sales_invoice SET bal_amt = 100 WHERE id = @Id", inv.Id);
        var paid = () => Service.UpdateAsync(inv.Id, Saved(saved, Keep(saved.Lines[0]), Keep(saved.Lines[1])) with { Remarks = "x" }, "kim", None);
        (await paid.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(inv.DnNo);
    }

    [DatabaseFact, Trait("Id", "SD-UT-131"), Trait("Id", "BC-UT-425"), Trait("Rule", "BC-BR-425")]
    public async Task DirectInvoice_TakesTheRequestCurrency_AndCountsForTheDocumentCurrencies()
    {
        var m = await MastersAsync(ShipThenBill);
        await ReceiveAsync(m.Item, m.Main, 5m);
        var sh = await Shipments.CreateAsync(ShipmentRequest(m, ShipmentLine(m.Item, 1m)), "kim", None);

        var krw = await Service.CreateAsync(Request(m, Direct(m.Service, 1m, 30000m, m.NoTax)) with { CurrencyCode = "krw", ExchangeRate = 1300m }, "kim", None);
        var mixed = () => Service.UpdateAsync(krw.Id, Saved(krw, Keep(krw.Lines[0]), FromShipment(sh.Lines[0].Id, 1m)), "kim", None);

        (krw.CurrencyCode, krw.ExchangeRate, krw.TotDnAmt).Should().Be(("KRW", 1300m, 30000m));
        (await mixed.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("KRW", "USD");
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        (await db.Get<ILocalizationSettingRepository>().FindDocumentCurrenciesAsync(DecimalModule.Sd, uow.Transaction, None))
            .Should().Contain("KRW");
    }

    private static SaveSalesInvoiceRequest Request(Masters m, params SaveSalesInvoiceLineRequest[] lines) =>
        new(m.Customer, DnDt, null, null, null, null, "DEMO_A", null, null, null, "Ship " + m.Key, "1 Main St", null, "Dallas", "TX", "75001",
            "USA", null, null, null, lines, null, UpdateCustomerShipTo: false);

    // A PUT sends the header as the screen shows it
    private static SaveSalesInvoiceRequest Saved(SalesInvoiceDto d, params SaveSalesInvoiceLineRequest[] lines) =>
        new(d.CustomerId, d.DnDt, d.DueDt, d.CurrencyCode, d.ExchangeRate, d.WarehouseId, d.Rep, d.Terms, d.ShipVia, d.FreightCharge, d.ShipName,
            d.ShipAddr1, d.ShipAddr2, d.ShipCity, d.ShipState, d.ShipZipCode, d.ShipCountry, d.ShipPhone, d.ShipFax, d.Remarks, lines, d.UpdatedAt,
            UpdateCustomerShipTo: false);

    private static SaveSalesInvoiceLineRequest Direct(long item, decimal qty, decimal price, long taxCode) =>
        new(null, item, null, qty, price, null, taxCode, null, null, null, null, null);

    private static SaveSalesInvoiceLineRequest FromShipment(long shipmentLineId, decimal qty) =>
        new(null, 0, null, qty, null, null, null, null, null, null, shipmentLineId, null);

    private static SaveSalesInvoiceLineRequest FromOrder(long orderLineId, decimal qty) =>
        new(null, 0, null, qty, null, null, null, null, null, null, null, orderLineId);

    private static SaveSalesInvoiceLineRequest Keep(SalesInvoiceLineDto l) =>
        new(l.Id, l.ItemId, l.ItemName, l.DnQty, l.DnPrc, l.DcCd, l.TaxCodeId, l.WarehouseId, l.LotId, l.LotNo, l.ShipmentLineId, l.SalesOrderLineId);

    private static SaveShipmentRequest ShipmentRequest(Masters m, params SaveShipmentLineRequest[] lines) =>
        new(m.Customer, DnDt, null, null, null, null, "Ship " + m.Key, "1 Main St", null, "Dallas", "TX", "75001", "USA", null, null, lines, null);

    private static SaveShipmentLineRequest ShipmentLine(long item, decimal qty) => new(null, item, null, null, qty, 100m, null, null, null, null, null);

    private sealed record Masters(string Key, long Customer, long Item, long Service, long Main, long Consignment, long Vat10, long NoTax);

    private sealed record Order(long Id, string SoNo, long Line);

    // A USD customer with this billing flow and a 10% VAT default tax code, an inventory item and a service; the rep code DEMO_A
    private async Task<Masters> MastersAsync(string billingFlow)
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(
            """
            UPDATE mm_inventory_setting SET negative_stock_mode = 'DENY';
            INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
            SELECT m.id, 'DEMO_A', 1, 't', 't' FROM sys_code_major m
            WHERE m.major_code = 'SALES_REP' AND NOT EXISTS (SELECT 1 FROM sys_code_minor n WHERE n.major_id = m.id AND n.minor_code = 'DEMO_A');
            """);
        var ids = await connection.QuerySingleAsync<(long Vat10, long NoTax, long Customer, long Item, long Service, long Main, long Consignment)>(
            """
            WITH t AS (INSERT INTO bc_tax_code (tax_code, tax_kind, rate, created_by, updated_by)
                       VALUES ('V' || @Key, 'VAT_STANDARD', 10, 't', 't') RETURNING id),
                 c AS (INSERT INTO bc_customer (c_name, terms, billing_flow, currency_code, tax_code_id, created_by, updated_by)
                       SELECT 'Customer ' || @Key, 'NET_30', @Flow, 'USD', id, 't', 't' FROM t RETURNING id),
                 i AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('I' || @Key, 'Bolt', 'INVENTORY', 't', 't') RETURNING id),
                 s AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('S' || @Key, 'Freight', 'SERVICE', 't', 't') RETURNING id)
            SELECT (SELECT id FROM t), (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), (SELECT id FROM c), (SELECT id FROM i),
                   (SELECT id FROM s), (SELECT id FROM bc_warehouse WHERE is_default),
                   (SELECT id FROM bc_warehouse WHERE warehouse_type = 'CONSIGNMENT')
            """,
            new { Key = key, Flow = billingFlow });
        return new Masters(key, ids.Customer, ids.Item, ids.Service, ids.Main, ids.Consignment, ids.Vat10, ids.NoTax);
    }

    // A USD order of the customer with one line of the item, without discount and tax
    private async Task<Order> OrderAsync(Masters m, string billingFlow, decimal qty, decimal price)
    {
        var soNo = "SO" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Id, long Line)>(
            """
            WITH o AS (INSERT INTO sd_sales_order (so_no, status, customer_id, so_dt, due_dt, currency_code, exchange_rate, billing_flow, rep, terms,
                                                   business_place_id, created_by, updated_by)
                       SELECT @SoNo, 'SO', @Customer, @SoDt, @SoDt, 'USD', 1, @Flow, 'DEMO_A', 'NET_30', id, 't', 't'
                       FROM bc_business_place WHERE is_default RETURNING id),
                 l AS (INSERT INTO sd_sales_order_line (sales_order_id, seq, item_id, so_qty, so_prc, so_amt, dc_rate, tax_code_id, dn_qty,
                                                        created_by, updated_by)
                       SELECT id, 1, @Item, @Qty, @Price, @Qty * @Price, 0, @NoTax, 0, 't', 't' FROM o RETURNING id)
            SELECT (SELECT id FROM o), (SELECT id FROM l)
            """,
            new { SoNo = soNo, m.Customer, SoDt = DnDt, Flow = billingFlow, m.Item, Qty = qty, Price = price, m.NoTax });
        return new Order(ids.Id, soNo, ids.Line);
    }

    // Receives stock at a cost of 50 on the invoice date
    private async Task ReceiveAsync(long item, long warehouse, decimal qty)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await Inventory.PostAsync([new StockMovementRequest("GR", DnDt, item, warehouse, qty, 50m, "GR-TEST", 1)], "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
    }

    private async Task<decimal> OnHandAsync(long item, long warehouse) =>
        (await QueryAsync<decimal?>(
            "SELECT onhand_qty FROM mm_onhand WHERE item_id = @Item AND warehouse_id = @Warehouse", new { Item = item, Warehouse = warehouse }))
        .SingleOrDefault() ?? 0m;

    // The item's stock value: the amount of all its movements
    private async Task<decimal> StockValueAsync(long item) =>
        (await QueryAsync<decimal>("SELECT coalesce(sum(amount), 0) FROM mm_goods_movement WHERE item_id = @Item", new { Item = item })).Single();

    private async Task<decimal> OrderLineDnQtyAsync(long id) =>
        (await QueryAsync<decimal>("SELECT coalesce(dn_qty, 0) FROM sd_sales_order_line WHERE id = @Id", new { Id = id })).Single();

    private async Task<(decimal BilledQty, string Status)> ShipmentLineAsync(long id) =>
        (await QueryAsync<(decimal, string)>(
            "SELECT l.billed_qty, h.status FROM sd_shipment_line l JOIN sd_shipment h ON h.id = l.shipment_id WHERE l.id = @Id", new { Id = id }))
        .Single();

    private async Task<(string Type, string Status, decimal TotDnAmt, decimal BalAmt, string? Remarks)> CreditMemoAsync(long id) =>
        (await QueryAsync<(string, string, decimal, decimal, string?)>(
            "SELECT type, status, tot_dn_amt, bal_amt, remarks FROM sd_sales_invoice WHERE id = @Id", new { Id = id }))
        .Single();

    // Movements of a document in id order: type, line, qty, amount and, for a reversal, its date
    private async Task<List<(string MovType, int RefSeq, decimal Qty, decimal Amount, DateOnly? ReversalDt)>> MovementsAsync(string no) =>
        [.. (await QueryAsync<(string, int, decimal, decimal, DateTime?)>(
                """
                SELECT mov_type, ref_seq, qty, amount, CASE WHEN parent_movement_id IS NULL THEN NULL ELSE document_dt END
                FROM mm_goods_movement WHERE ref_key = @No ORDER BY id
                """, new { No = no }))
            .Select(r => (r.Item1, r.Item2, r.Item3, r.Item4, r.Item5 is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null))];

    private async Task<int> CountAsync(string sql, long id) => (await QueryAsync<int>(sql, new { Id = id })).Single();

    private async Task ExecuteAsync(string sql, long id)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    private async Task<List<T>> QueryAsync<T>(string sql, object parameters)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return [.. await connection.QueryAsync<T>(sql, parameters)];
    }

    [GeneratedRegex(@"^DN\d{7}$")]
    private static partial Regex InvoiceNumber();

    [GeneratedRegex(@"^CM2611\d{6}$")]
    private static partial Regex CreditMemoNumber();

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => SalesInvoiceServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}

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
using QuroErp.Application.Sd.Shipments;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;
using QuroErp.Domain.Sd.SalesOrders;
using QuroErp.Domain.Sd.Shipments;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Sd.Shipments;

/// <summary>Own schema for shipment tests: the services commit, so their rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class ShipmentSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "ShipmentSchema";
}

/// <summary>
/// ShipmentService with the Dapper repositories and the inventory service on real PostgreSQL (SD-UC-051). Without a company the
/// base currency is USD. Today is <see cref="Today"/>. Each test makes its own customer, items and orders, receives the stock it
/// ships at a cost of 50 and sets the negative stock mode it needs.
/// </summary>
[Collection(ShipmentSchemaDefinition.Name)]
public partial class ShipmentServiceTests(AuthSchemaFixture db)
{
    private const string ShipAndBill = "SHIP_AND_BILL";
    private const string ShipThenBill = "SHIP_THEN_BILL";
    private const string BillThenShip = "BILL_THEN_SHIP";
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly ShipDt = new(2026, 11, 4);
    private static readonly DateOnly Today = new(2026, 11, 10);

    private ShipmentService Service => new(
        db.Get<IShipmentRepository>(), db.Get<ICustomerRepository>(), db.Get<ITaxRepository>(), new TaxAreaRateProvider(db.Get<ITaxRepository>()),
        db.Get<ILocalizationRepository>(), db.Get<IBusinessPlaceRepository>(), db.Get<IWarehouseRepository>(),
        new ExchangeRateService(
            db.Get<IExchangeRateRepository>(), db.Get<ICurrencyRepository>(), db.Get<ILocalizationRepository>(), new FixedClock(),
            db.Get<IUnitOfWorkFactory>()),
        Inventory, new DocumentNumberService(db.Get<IDocumentNumberRepository>()),
        new CodeValidator(db.Get<ICodeRepository>(), db.Get<IUnitOfWorkFactory>()), new FixedClock(), db.Get<IUnitOfWorkFactory>());

    private InventoryService Inventory => new(
        db.Get<IInventoryRepository>(), db.Get<IInventorySettingRepository>(), db.Get<ISetComponentRepository>(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    [DatabaseFact, Trait("Id", "SD-UC-051-T01"), Trait("Id", "SD-UT-163"), Trait("Id", "SD-UT-164"), Trait("Id", "SD-UT-165"),
     Trait("Id", "SD-UT-167"), Trait("Id", "SD-UT-173")]
    public async Task Create_MixesOrderAndDirectLines_IssuesStock_AndBillsBothLines()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        var so = await OrderAsync(m, ShipAndBill, (m.Item, 10m, 100m, -10m, m.Vat10));

        var sh = await Service.CreateAsync(Request(m, OrderLine(so.Lines[0], m.Item, 4m), Direct(m.Item, 2m, 120m, m.NoTax)), "kim", None);

        ShipNumber().IsMatch(sh.ShipNo).Should().BeTrue(sh.ShipNo);
        (sh.Status, sh.BillingFlow, sh.SalesOrderId, sh.CurrencyCode, sh.WarehouseId).Should().Be(("BL", ShipAndBill, (long?)so.Id, "USD", m.Main));
        sh.Lines.Select(l => (l.Seq, l.ShipQty, l.ShipPrc, l.DcRate, l.TaxCodeId, l.BilledQty, l.SalesOrderLineId, l.SoNo))
            .Should().Equal((1, 4m, 100m, -10m, m.Vat10, 4m, (long?)so.Lines[0], so.SoNo), (2, 2m, 120m, 0m, m.NoTax, 2m, null, null));
        sh.Lines[0].OnhandQty.Should().Be(14m);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(14m);
        (await MovementsAsync(sh.ShipNo)).Should().Equal(("DN", 1, -4m, -200m, (DateOnly?)null), ("DN", 2, -2m, -100m, null));
        (await OrderLineAsync(so.Lines[0])).Should().Be((4m, 4m));
        (await OrderStatusAsync(so.Id)).Should().Be("RE");

        var invoice = await InvoiceAsync(sh.SalesInvoiceId!.Value);
        sh.SalesInvoiceNo.Should().StartWith("DN").And.HaveLength(9);
        (invoice.DnDt, invoice.Status, invoice.SupplyAmt, invoice.TaxAmt, invoice.TotDnAmt, invoice.BalAmt, invoice.BillingFlow)
            .Should().Be((ShipDt, "OP", 600m, 36m, 636m, 636m, ShipAndBill));
        (await InvoiceLinesAsync(invoice.Id)).Should().Equal(
            (1, 4m, 360m, 36m, (long?)so.Lines[0], sh.Lines[0].Id), (2, 2m, 240m, 0m, null, sh.Lines[1].Id));
        (await Service.SearchAsync(new(sh.ShipNo, m.Customer, null, ShipDt, ShipDt), None))
            .Should().ContainSingle(s => s.Id == sh.Id && s.SalesInvoiceNo == sh.SalesInvoiceNo);
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T02"), Trait("Id", "SD-UT-169"), Trait("Id", "SD-UT-104")]
    public async Task ShipAndBill_VoidTakesEverythingBack_AndAPaidInvoiceLocksTheShipment()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        var so = await OrderAsync(m, ShipAndBill, (m.Item, 5m, 100m, 0m, m.NoTax));
        var paid = await Service.CreateAsync(Request(m, OrderLine(so.Lines[0], m.Item, 2m)), "kim", None);
        var open = await Service.CreateAsync(Request(m, OrderLine(so.Lines[0], m.Item, 3m)), "kim", None);
        (await OrderStatusAsync(so.Id)).Should().Be("CL");
        await ExecuteAsync("UPDATE sd_sales_invoice SET bal_amt = 50 WHERE id = @Id", paid.SalesInvoiceId!.Value);

        var update = () => Service.UpdateAsync(paid.Id, Saved(paid, Keep(paid.Lines[0])), "kim", None);
        var voidPaid = () => Service.VoidAsync(paid.Id, paid.UpdatedAt, "kim", None);
        var voided = await Service.VoidAsync(open.Id, open.UpdatedAt, "kim", None);

        (await update.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(paid.SalesInvoiceNo!);
        (await voidPaid.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ShipmentRules.InvoicePaidCode);
        (voided.Status, voided.Lines[0].BilledQty).Should().Be(("VO", 0m));
        var invoice = await InvoiceAsync(open.SalesInvoiceId!.Value);
        (invoice.Status, invoice.TotDnAmt, invoice.BalAmt).Should().Be(("VO", 0m, 0m));
        (await OnHandAsync(m.Item, m.Main)).Should().Be(18m);
        (await MovementsAsync(open.ShipNo)).Should().Equal(("DN", 1, -3m, -150m, null), ("DN", 1, 3m, 150m, Today));
        (await OrderLineAsync(so.Lines[0])).Should().Be((2m, 2m));
        (await OrderStatusAsync(so.Id)).Should().Be("RE");
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T03"), Trait("Id", "SD-UT-168"), Trait("Id", "SD-UT-166")]
    public async Task ShipThenBill_MakesNoInvoice_AndItsLinesWaitToBeBilled()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipThenBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        var so = await OrderAsync(m, ShipThenBill, (m.Item, 10m, 100m, 0m, m.Vat10));

        var first = await Service.CreateAsync(Request(m, OrderLine(so.Lines[0], m.Item, 3m)), "kim", None);
        var second = await Service.CreateAsync(Request(m, OrderLine(so.Lines[0], m.Item, 2m)), "kim", None);
        // The sales invoice task bills them; here the billed qty is set as it would be (SD-BR-108)
        await ExecuteAsync("UPDATE sd_shipment_line SET billed_qty = ship_qty WHERE shipment_id = @Id", first.Id);

        (first.Status, first.SalesInvoiceId, first.Lines[0].BilledQty).Should().Be(("OP", null, 0m));
        (await OrderLineAsync(so.Lines[0])).Should().Be((5m, 0m));
        (await Service.UnbilledLinesAsync(m.Customer, "usd", None)).Select(l => (l.ShipNo, l.UnbilledQty, l.SoNo, l.TaxCodeId))
            .Should().Equal((second.ShipNo, 2m, so.SoNo, m.Vat10));
        (await Service.UnbilledLinesAsync(m.Customer, "EUR", None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T04"), Trait("Id", "SD-UT-168"), Trait("Id", "SD-UT-171")]
    public async Task ShipThenBill_ABilledLineCannotBeDeletedOrVoided_AndSavedLinesCannotChange()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipThenBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        var sh = await Service.CreateAsync(Request(m, Direct(m.Item, 3m), Direct(m.Item, 1m)), "kim", None);
        await ExecuteAsync("UPDATE sd_shipment_line SET billed_qty = 1 WHERE shipment_id = @Id AND seq = 1", sh.Id);

        var delete = () => Service.UpdateAsync(sh.Id, Saved(sh, Keep(sh.Lines[1])), "kim", None);
        var voidIt = () => Service.VoidAsync(sh.Id, sh.UpdatedAt, "kim", None);
        var change = () => Service.UpdateAsync(sh.Id, Saved(sh, Keep(sh.Lines[0]), Keep(sh.Lines[1]) with { ShipQty = 2m }), "kim", None);

        (await delete.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(sh.ShipNo, 1);
        (await voidIt.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ShipmentRules.BilledCode);
        (await change.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ShipmentRules.LineChangedCode);
        var kept = await Service.UpdateAsync(sh.Id, Saved(sh, Keep(sh.Lines[0])), "kim", None);
        kept.Lines.Select(l => (l.Seq, l.BilledQty)).Should().Equal((1, 1m));
        kept.Status.Should().Be("PB");
        (await OnHandAsync(m.Item, m.Main)).Should().Be(17m);
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T05"), Trait("Id", "SD-UT-174"), Trait("Id", "SD-UT-172")]
    public async Task BillThenShipOrders_ShipOnlyThroughTheirInvoice()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        var billFirst = await OrderAsync(m, BillThenShip, (m.Item, 5m, 100m, 0m, m.NoTax));
        var closed = await OrderAsync(m, ShipAndBill, (m.Item, 5m, 100m, 0m, m.NoTax));
        await ExecuteAsync("UPDATE sd_sales_order SET status = 'CL' WHERE id = @Id", closed.Id);
        var open = await OrderAsync(m, ShipThenBill, (m.Item, 5m, 100m, 0m, m.NoTax), (m.Service, 1m, 10m, 0m, m.NoTax));

        var act = () => Service.CreateAsync(Request(m, OrderLine(billFirst.Lines[0], m.Item, 1m)), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(billFirst.SoNo);
        var orders = await Service.OpenOrderLinesAsync(m.Customer, None);
        orders.Should().ContainSingle().Which.SoNo.Should().Be(open.SoNo);
        orders[0].Lines.Select(l => (l.Seq, l.RemainingQty)).Should().Equal((1, 5m), (2, 1m));
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T06"), Trait("Id", "SD-UT-175"), Trait("Id", "SD-UT-176"), Trait("Id", "SD-UT-177")]
    public async Task BillThenShip_IssuesFromTheConsignmentWarehouse_UpToTheUnshippedQty()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(BillThenShip);
        await ReceiveAsync(m.Item, m.Consignment, 6m);
        var so = await OrderAsync(m, BillThenShip, (m.Item, 6m, 100m, 0m, m.NoTax));
        var invoice = await BillFirstInvoiceAsync(m, so.Lines[0], m.Item, 6m, 100m);

        var over = () => Service.CreateAsync(Request(m, InvoiceLine(invoice.Line, m.Item, 7m)), "kim", None);
        var mixed = () => Service.CreateAsync(Request(m, InvoiceLine(invoice.Line, m.Item, 1m), Direct(m.Item, 1m)), "kim", None);
        (await over.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(invoice.DnNo, 1, 6m);
        (await mixed.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ShipmentRules.InvoiceLineCode);

        var sh = await Service.CreateAsync(Request(m, InvoiceLine(invoice.Line, m.Item, 4m) with { WarehouseId = m.Main }), "kim", None);

        (sh.Status, sh.BillingFlow, sh.SalesInvoiceId, sh.WarehouseId).Should().Be(("BL", BillThenShip, null, m.Consignment));
        sh.Lines.Select(l => (l.WarehouseId, l.BilledQty, l.SalesInvoiceLineId, l.DnNo, l.SalesOrderLineId))
            .Should().Equal((m.Consignment, 4m, (long?)invoice.Line, invoice.DnNo, (long?)so.Lines[0]));
        (await OnHandAsync(m.Item, m.Consignment)).Should().Be(2m);
        (await ShippedQtyAsync(invoice.Line)).Should().Be(4m);
        (await OrderLineAsync(so.Lines[0])).Should().Be((4m, 6m), "the invoice billed the order line");
        (await Service.UnshippedInvoiceLinesAsync(m.Customer, None)).Select(l => (l.DnNo, l.UnshippedQty)).Should().Equal((invoice.DnNo, 2m));

        // SD-BR-177: a void ignores the invoice's receipts and gives the stock back to the consignment warehouse
        await ExecuteAsync("UPDATE sd_sales_invoice SET bal_amt = 0 WHERE id = @Id", invoice.Id);
        await Service.VoidAsync(sh.Id, sh.UpdatedAt, "kim", None);
        (await OnHandAsync(m.Item, m.Consignment)).Should().Be(6m);
        (await ShippedQtyAsync(invoice.Line)).Should().Be(0m);
        (await OrderLineAsync(so.Lines[0])).Should().Be((0m, 6m));
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T07"), Trait("Id", "SD-UT-162"), Trait("Id", "SD-UT-165")]
    public async Task TheRemainingQty_LimitsTheShipment_AndAFullShipmentClosesTheOrder()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipThenBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        var so = await OrderAsync(m, ShipThenBill, (m.Item, 5m, 100m, 0m, m.NoTax));
        var first = await Service.CreateAsync(Request(m, OrderLine(so.Lines[0], m.Item, 3m)), "kim", None);

        var over = () => Service.CreateAsync(Request(m, OrderLine(so.Lines[0], m.Item, 3m)), "kim", None);
        (await over.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(so.SoNo, 1, 2m);

        // The line this save deletes gives its qty back first
        var replaced = await Service.UpdateAsync(first.Id, Saved(first, OrderLine(so.Lines[0], m.Item, 5m)), "kim", None);
        replaced.Lines.Select(l => (l.Seq, l.ShipQty)).Should().Equal((2, 5m));
        (await OrderStatusAsync(so.Id)).Should().Be("CL");
        (await Service.OpenOrderLinesAsync(m.Customer, None)).Should().BeEmpty();

        await Service.UpdateAsync(first.Id, Saved(replaced), "kim", None);
        (await OrderLineAsync(so.Lines[0])).Should().Be((0m, 0m));
        (await OrderStatusAsync(so.Id)).Should().Be("SO");
        (await OnHandAsync(m.Item, m.Main)).Should().Be(20m);
    }

    [DatabaseFact, Trait("Id", "SD-UT-161"), Trait("Id", "SD-UT-170"), Trait("Id", "SD-UT-163"), Trait("Id", "MM-UT-710")]
    public async Task Create_RefusesBadLines_AnotherCurrency_AZeroCost_AndTheConsignmentWarehouse()
    {
        await ModeAsync("ALLOW");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        var euro = await OrderAsync(m, ShipAndBill, (m.Item, 5m, 100m, 0m, m.NoTax));
        await ExecuteAsync("UPDATE sd_sales_order SET currency_code = 'EUR', exchange_rate = 1.1 WHERE id = @Id", euro.Id);

        var zeroQty = () => Service.CreateAsync(Request(m, Direct(m.Item, 0m)), "kim", None);
        var zeroPrice = () => Service.CreateAsync(Request(m, Direct(m.Item, 1m, 0m)), "kim", None);
        var usd = await Service.CreateAsync(Request(m, Direct(m.Item, 1m)), "kim", None);
        var currency = () => Service.UpdateAsync(usd.Id, Saved(usd, Keep(usd.Lines[0]), OrderLine(euro.Lines[0], m.Item, 1m)), "kim", None);
        var noCost = () => Service.CreateAsync(Request(m, Direct(m.Item, 1m), Direct(m.NoCostItem, 1m)), "kim", None);
        var consignment = () => Service.CreateAsync(Request(m, Direct(m.Item, 1m) with { WarehouseId = m.Consignment }), "kim", None);

        (await zeroQty.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ShipmentRules.QtyPriceCode);
        (await zeroPrice.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(1);
        (await currency.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("USD", "EUR");
        (await noCost.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("N" + m.Key);
        (await consignment.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-710");
        (await OnHandAsync(m.Item, m.Main)).Should().Be(19m, "every refused save rolled back");
    }

    [DatabaseFact, Trait("Id", "SD-UT-309")]
    public async Task CreditLimit_IsAskedUntilConfirmed()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 20m);
        // The balance is what the customer's invoices still owe
        await ExecuteAsync(
            """
            UPDATE bc_customer SET credit_limit = 500 WHERE id = @Id;
            INSERT INTO sd_sales_invoice (dn_no, customer_id, dn_dt, tot_dn_amt, bal_amt, type, status, currency_code, exchange_rate, warehouse_id,
                                          business_place_id, created_by, updated_by)
            SELECT 'DN' || @Id, @Id, '2026-11-01', 300, 300, 'IV', 'OP', 'USD', 1, (SELECT id FROM bc_warehouse WHERE is_default), id, 't', 't'
            FROM bc_business_place WHERE is_default
            """, m.Customer);

        var act = () => Service.CreateAsync(Request(m, Direct(m.Item, 3m, 100m, m.NoTax)), "kim", None);
        var asked = (await act.Should().ThrowAsync<ConfirmationRequiredException>()).Which;
        var saved = await Service.CreateAsync(Request(m, Direct(m.Item, 3m, 100m, m.NoTax)) with { ConfirmCreditLimit = true }, "kim", None);

        (asked.Code, asked.ConfirmField).Should().Be((SalesOrderEntryRules.CreditLimitWarningCode, SalesOrderEntryRules.ConfirmCreditLimitField));
        asked.Args.Should().Equal(500m, 600m);
        saved.Status.Should().Be("BL");
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T09"), Trait("Id", "MM-UT-355"), Trait("Id", "MM-UT-356")]
    public async Task LotItems_NeedALot_AndALotBalanceBelowZeroIsRefusedEvenInAllowInMonth()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.LotItem, m.Main, 5m, "A1");
        await ReceiveAsync(m.LotItem, m.Main, 5m, "B1");

        var noLot = () => Service.CreateAsync(Request(m, Direct(m.LotItem, 1m)), "kim", None);
        (await noLot.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LotRules.RequiredCode);

        await ModeAsync("ALLOW_IN_MONTH");
        var tooMuch = () => Service.CreateAsync(Request(m, Direct(m.LotItem, 6m) with { LotNo = "a1" }), "kim", None);
        var sh = await Service.CreateAsync(Request(m, Direct(m.LotItem, 2m) with { LotNo = "b1" }), "kim", None);

        (await tooMuch.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LotRules.NegativeCode);
        sh.Lines.Single().LotNo.Should().Be("B1");
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T10"), Trait("Id", "MM-UT-001")]
    public async Task Deny_RefusesAnIssueOverTheWarehouseOnHand()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 3m);

        var act = () => Service.CreateAsync(Request(m, Direct(m.Item, 5m)), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(StockPolicy.InsufficientStock);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(3m);
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T11"), Trait("Id", "MM-UT-002")]
    public async Task AllowInMonth_SavesAMidMonthNegative_AndMarksIt()
    {
        await ModeAsync("ALLOW_IN_MONTH");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 10m);
        await ReceiveAsync(m.Item, m.Main, 10m, date: new(2026, 11, 20));

        var sh = await Service.CreateAsync(Request(m, Direct(m.Item, 15m)) with { ShipDt = new(2026, 11, 5) }, "kim", None);

        (await CountAsync("SELECT count(*) FROM mm_goods_movement WHERE ref_key = (SELECT @No) AND negative_stock", sh.ShipNo)).Should().Be(1);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(5m);
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T12"), Trait("Id", "MM-UT-009")]
    public async Task Deny_RefusesABackDatedIssueThatMakesALaterDateNegative()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 10m);
        await Service.CreateAsync(Request(m, Direct(m.Item, 10m)) with { ShipDt = new(2026, 11, 20) }, "kim", None);
        await ReceiveAsync(m.Item, m.Main, 10m, date: new(2026, 11, 25));

        var act = () => Service.CreateAsync(Request(m, Direct(m.Item, 5m)) with { ShipDt = new(2026, 11, 10) }, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PointInTimeStock.NegativeOnDate);
    }

    [DatabaseFact, Trait("Id", "SD-UC-051-T13"), Trait("Id", "MM-UT-010")]
    public async Task AllowInMonth_RefusesANegativeMonthEnd_WithNoMovementOnTheLastDay()
    {
        await ModeAsync("ALLOW_IN_MONTH");
        var m = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 10m);

        var act = () => Service.CreateAsync(Request(m, Direct(m.Item, 15m)) with { ShipDt = new(2026, 11, 5) }, "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PointInTimeStock.NegativeAtMonthEnd);
    }

    [DatabaseFact]
    public async Task StaleUpdatedAt_Is409_AndAnotherCustomerIsRefused()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipAndBill);
        var other = await MastersAsync(ShipAndBill);
        await ReceiveAsync(m.Item, m.Main, 10m);
        var sh = await Service.CreateAsync(Request(m, Direct(m.Item, 1m)), "kim", None);
        var saved = await Service.UpdateAsync(sh.Id, Saved(sh, Keep(sh.Lines[0])) with { Remarks = "x" }, "kim", None);

        var stale = () => Service.UpdateAsync(sh.Id, Saved(sh, Keep(sh.Lines[0])), "kim", None);
        var customer = () => Service.UpdateAsync(sh.Id, Saved(saved, Keep(saved.Lines[0])) with { CustomerId = other.Customer }, "kim", None);

        saved.Remarks.Should().Be("x");
        await stale.Should().ThrowAsync<ConcurrencyException>();
        (await customer.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesOrderEntryRules.CustomerLockedCode);
    }

    [DatabaseFact, Trait("Id", "BC-UT-425"), Trait("Rule", "BC-BR-425")]
    public async Task ShipmentLines_CountForTheirDocumentCurrency()
    {
        await ModeAsync("DENY");
        var m = await MastersAsync(ShipThenBill);
        await ReceiveAsync(m.Item, m.Main, 1m);
        await Service.CreateAsync(Request(m, Direct(m.Item, 1m)), "kim", None);

        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        (await db.Get<ILocalizationSettingRepository>().FindDocumentCurrenciesAsync(DecimalModule.Sd, uow.Transaction, None))
            .Should().Contain("USD");
    }

    private static SaveShipmentRequest Request(Masters m, params SaveShipmentLineRequest[] lines) =>
        new(m.Customer, ShipDt, null, null, null, null, "Ship " + m.Key, "1 Main St", null, "Dallas", "TX", "75001", "USA", null, null, lines, null);

    // A PUT sends the header as the screen shows it
    private static SaveShipmentRequest Saved(ShipmentDto sh, params SaveShipmentLineRequest[] lines) =>
        new(sh.CustomerId, sh.ShipDt, sh.WarehouseId, sh.CurrencyCode, sh.ExchangeRate, sh.ShipVia, sh.ShipName, sh.ShipAddr1, sh.ShipAddr2,
            sh.ShipCity, sh.ShipState, sh.ShipZipCode, sh.ShipCountry, sh.ShipPhone, sh.Remarks, lines, sh.UpdatedAt);

    private static SaveShipmentLineRequest Direct(long item, decimal qty, decimal price = 100m, long? taxCode = null) =>
        new(null, item, null, null, qty, price, taxCode, null, null, null, null);

    private static SaveShipmentLineRequest OrderLine(long orderLineId, long item, decimal qty) =>
        new(null, item, null, null, qty, null, null, null, null, orderLineId, null);

    private static SaveShipmentLineRequest InvoiceLine(long invoiceLineId, long item, decimal qty) =>
        new(null, item, null, null, qty, null, null, null, null, null, invoiceLineId);

    private static SaveShipmentLineRequest Keep(ShipmentLineDto l) =>
        new(l.Id, l.ItemId, l.ItemName, l.WarehouseId, l.ShipQty, l.ShipPrc, l.TaxCodeId, l.LotId, null, l.SalesOrderLineId, l.SalesInvoiceLineId);

    private sealed record Masters(
        string Key, long Customer, long Item, long Service, long LotItem, long NoCostItem, long Main, long Consignment, long Vat10, long NoTax);

    private sealed record Order(long Id, string SoNo, IReadOnlyList<long> Lines);

    private sealed record BillFirstInvoice(long Id, string DnNo, long Line);

    private sealed record Invoice(
        long Id, DateOnly DnDt, string Status, decimal SupplyAmt, decimal TaxAmt, decimal TotDnAmt, decimal BalAmt, string BillingFlow);

    // A USD customer with this billing flow and a 10% VAT default tax code; an inventory, a service, a lot-managed item and an
    // inventory item that is never received (moving average cost 0)
    private async Task<Masters> MastersAsync(string billingFlow)
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Vat10, long NoTax, long Customer, long Item, long Service, long LotItem, long NoCost, long Main, long Consignment)>(
            """
            WITH t AS (INSERT INTO bc_tax_code (tax_code, tax_kind, rate, created_by, updated_by)
                       VALUES ('V' || @Key, 'VAT_STANDARD', 10, 't', 't') RETURNING id),
                 c AS (INSERT INTO bc_customer (c_name, terms, billing_flow, currency_code, tax_code_id, created_by, updated_by)
                       SELECT 'Customer ' || @Key, 'NET_30', @Flow, 'USD', id, 't', 't' FROM t RETURNING id),
                 i AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('I' || @Key, 'Bolt', 'INVENTORY', 't', 't') RETURNING id),
                 s AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('S' || @Key, 'Freight', 'SERVICE', 't', 't') RETURNING id),
                 l AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, created_by, updated_by)
                       VALUES ('L' || @Key, 'Paint', 'INVENTORY', true, '2026-10-01', 't', 't') RETURNING id),
                 n AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('N' || @Key, 'Nut', 'INVENTORY', 't', 't') RETURNING id)
            SELECT (SELECT id FROM t), (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), (SELECT id FROM c), (SELECT id FROM i),
                   (SELECT id FROM s), (SELECT id FROM l), (SELECT id FROM n), (SELECT id FROM bc_warehouse WHERE is_default),
                   (SELECT id FROM bc_warehouse WHERE warehouse_type = 'CONSIGNMENT')
            """,
            new { Key = key, Flow = billingFlow });
        return new Masters(
            key, ids.Customer, ids.Item, ids.Service, ids.LotItem, ids.NoCost, ids.Main, ids.Consignment, ids.Vat10, ids.NoTax);
    }

    // A USD order of the customer with these lines (item, qty, price, discount rate, tax code)
    private async Task<Order> OrderAsync(Masters m, string billingFlow, params (long Item, decimal Qty, decimal Price, decimal DcRate, long TaxCode)[] lines)
    {
        var soNo = "SO" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var id = await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO sd_sales_order (so_no, status, customer_id, so_dt, due_dt, currency_code, exchange_rate, billing_flow, rep, terms,
                                        business_place_id, created_by, updated_by)
            SELECT @SoNo, 'SO', @Customer, @SoDt, @SoDt, 'USD', 1, @Flow, 'DEMO_A', 'NET_30', id, 't', 't'
            FROM bc_business_place WHERE is_default
            RETURNING id
            """,
            new { SoNo = soNo, m.Customer, SoDt = ShipDt, Flow = billingFlow });
        var lineIds = new List<long>();
        foreach (var (line, seq) in lines.Select((l, i) => (l, i + 1)))
        {
            lineIds.Add(await connection.ExecuteScalarAsync<long>(
                """
                INSERT INTO sd_sales_order_line (sales_order_id, seq, item_id, so_qty, so_prc, so_amt, dc_rate, dc_cd, tax_code_id, dn_qty,
                                                 created_by, updated_by)
                VALUES (@Id, @Seq, @Item, @Qty, @Price, @Qty * @Price, @DcRate, CASE WHEN @DcRate = 0 THEN NULL ELSE 'D10' END, @TaxCode, 0, 't', 't')
                RETURNING id
                """,
                new { Id = id, Seq = seq, line.Item, line.Qty, line.Price, line.DcRate, line.TaxCode }));
        }
        return new Order(id, soNo, lineIds);
    }

    // A BILL_THEN_SHIP invoice of one line for the order line, billed and not shipped yet (SD-BR-141, SD-BR-143)
    private async Task<BillFirstInvoice> BillFirstInvoiceAsync(Masters m, long orderLineId, long item, decimal qty, decimal price)
    {
        var dnNo = "DN" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Id, long Line)>(
            """
            WITH d AS (INSERT INTO sd_sales_invoice (dn_no, customer_id, dn_dt, tot_dn_amt, bal_amt, type, status, currency_code, exchange_rate,
                                                     warehouse_id, business_place_id, billing_flow, created_by, updated_by)
                       SELECT @DnNo, @Customer, @DnDt, @Qty * @Price, @Qty * @Price, 'IV', 'OP', 'USD', 1, @Main, id, 'BILL_THEN_SHIP', 't', 't'
                       FROM bc_business_place WHERE is_default RETURNING id),
                 l AS (INSERT INTO sd_sales_invoice_line (sales_invoice_id, seq, item_id, dn_qty, dn_prc, dn_amt, sales_order_line_id, warehouse_id,
                                                          tax_code_id, created_by, updated_by)
                       SELECT id, 1, @Item, @Qty, @Price, @Qty * @Price, @OrderLine, @Main, @NoTax, 't', 't' FROM d RETURNING id)
            SELECT (SELECT id FROM d), (SELECT id FROM l)
            """,
            new { DnNo = dnNo, m.Customer, DnDt = ShipDt, Qty = qty, Price = price, m.Main, Item = item, OrderLine = orderLineId, m.NoTax });
        await ExecuteAsync("UPDATE sd_sales_order_line SET dn_qty = so_qty WHERE id = @Id", orderLineId);
        return new BillFirstInvoice(ids.Id, dnNo, ids.Line);
    }

    // Receives stock at a cost of 50 on the ship date (or the date given)
    private async Task ReceiveAsync(long item, long warehouse, decimal qty, string? lotNo = null, DateOnly? date = null)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await Inventory.PostAsync([new StockMovementRequest("GR", date ?? ShipDt, item, warehouse, qty, 50m, "GR-TEST", 1, LotNo: lotNo)], "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
    }

    private async Task ModeAsync(string negativeStockMode)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync("UPDATE mm_inventory_setting SET negative_stock_mode = @Mode", new { Mode = negativeStockMode });
    }

    private async Task<decimal> OnHandAsync(long item, long warehouse) =>
        (await QueryAsync<decimal?>(
            "SELECT onhand_qty FROM mm_onhand WHERE item_id = @Item AND warehouse_id = @Warehouse", new { Item = item, Warehouse = warehouse }))
        .SingleOrDefault() ?? 0m;

    private async Task<(decimal ShipQty, decimal DnQty)> OrderLineAsync(long id) =>
        (await QueryAsync<(decimal, decimal)>("SELECT ship_qty, coalesce(dn_qty, 0) FROM sd_sales_order_line WHERE id = @Id", new { Id = id })).Single();

    private async Task<string> OrderStatusAsync(long id) =>
        (await QueryAsync<string>("SELECT status FROM sd_sales_order WHERE id = @Id", new { Id = id })).Single();

    private async Task<decimal> ShippedQtyAsync(long invoiceLineId) =>
        (await QueryAsync<decimal>("SELECT shipped_qty FROM sd_sales_invoice_line WHERE id = @Id", new { Id = invoiceLineId })).Single();

    private async Task<Invoice> InvoiceAsync(long id) =>
        (await QueryAsync<Invoice>(
            "SELECT id, dn_dt, status, supply_amt, tax_amt, tot_dn_amt, bal_amt, billing_flow FROM sd_sales_invoice WHERE id = @Id", new { Id = id }))
        .Single();

    // Invoice lines in seq order: seq, qty, amount, tax, order line and shipment line
    private async Task<List<(int Seq, decimal DnQty, decimal DnAmt, decimal TaxAmt, long? OrderLine, long? ShipmentLine)>> InvoiceLinesAsync(long id) =>
        await QueryAsync<(int, decimal, decimal, decimal, long?, long?)>(
            """
            SELECT seq, dn_qty, dn_amt, tax_amt, sales_order_line_id, shipment_line_id
            FROM sd_sales_invoice_line WHERE sales_invoice_id = @Id ORDER BY seq
            """, new { Id = id });

    // Movements of a shipment in id order: type, line, qty, amount and, for a reversal, its date
    private async Task<List<(string MovType, int RefSeq, decimal Qty, decimal Amount, DateOnly? ReversalDt)>> MovementsAsync(string shipNo) =>
        [.. (await QueryAsync<(string, int, decimal, decimal, DateTime?)>(
                """
                SELECT mov_type, ref_seq, qty, amount, CASE WHEN parent_movement_id IS NULL THEN NULL ELSE document_dt END
                FROM mm_goods_movement WHERE ref_key = @No ORDER BY id
                """, new { No = shipNo }))
            .Select(r => (r.Item1, r.Item2, r.Item3, r.Item4, r.Item5 is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null))];

    private async Task<int> CountAsync(string sql, string no) =>
        (await QueryAsync<int>(sql, new { No = no })).Single();

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

    [GeneratedRegex(@"^SH\d{7}$")]
    private static partial Regex ShipNumber();

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => ShipmentServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}

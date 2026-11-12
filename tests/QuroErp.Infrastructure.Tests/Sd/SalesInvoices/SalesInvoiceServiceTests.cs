using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Application.Bc.Companies;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Common.Printing;
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
using QuroErp.Domain.Sd.SalesOrders;
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
    private static readonly DateOnly AfterTheRateChange = new(2026, 11, 6);

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

    [DatabaseFact, Trait("Id", "SD-UT-141"), Trait("Id", "SD-UT-142"), Trait("Id", "SD-UT-143"), Trait("Id", "MM-UT-706"), Trait("Id", "MM-UT-709"), Trait("Id", "MM-UT-710")]
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

    [DatabaseFact, Trait("Id", "SD-UT-111"), Trait("Id", "SD-UT-135")]
    public async Task AnInvoiceLeavesTheStock_AndTaxAndCurrencyLeaveTheStockValue()
    {
        var m = await MastersAsync(ShipThenBill);
        await ReceiveAsync(m.Item, m.Main, 20m);

        // The shipment issues at the cost of 50 in the base currency, whatever the document's currency and tax
        var sh = await Shipments.CreateAsync(ShipmentRequest(m, ShipmentLine(m.Item, 3m)) with { CurrencyCode = "KRW", ExchangeRate = 1300m }, "kim", None);
        (await MovementsAsync(sh.ShipNo)).Select(x => (x.Qty, x.Amount)).Should().Equal((-3m, -150m));

        var inv = await Service.CreateAsync(Request(m, FromShipment(sh.Lines[0].Id, 2m)), "kim", None);

        (inv.CurrencyCode, inv.SupplyAmt, inv.TaxAmt).Should().Be(("KRW", 200m, 20m));
        (await MovementsAsync(inv.DnNo)).Should().BeEmpty();
        (await OnHandAsync(m.Item, m.Main)).Should().Be(17m);
        (await StockValueAsync(m.Item)).Should().Be(850m);
    }

    [DatabaseFact, Trait("Id", "SD-UT-117")]
    public async Task ADifferentShipTo_IsAskedFirst_AndOnlyYesUpdatesTheCustomer()
    {
        var m = await MastersAsync(ShipThenBill);
        var request = Request(m, Direct(m.Service, 1m, 30m, m.NoTax));

        var unanswered = () => Service.CreateAsync(request with { UpdateCustomerShipTo = null }, "kim", None);
        var asked = (await unanswered.Should().ThrowAsync<ConfirmationRequiredException>()).Which;
        (asked.Code, asked.ConfirmField).Should().Be((SalesOrderEntryRules.ShipToWarningCode, SalesOrderEntryRules.UpdateCustomerShipToField));
        (await CountAsync("SELECT count(*) FROM sd_sales_invoice WHERE customer_id = @Id", m.Customer)).Should().Be(0);

        await Service.CreateAsync(request with { UpdateCustomerShipTo = false }, "kim", None);
        (await CustomerShipNameAsync(m.Customer)).Should().BeNull();

        await Service.CreateAsync(request with { UpdateCustomerShipTo = true }, "kim", None);
        (await CustomerShipNameAsync(m.Customer)).Should().Be("Ship " + m.Key);

        // The customer now has this ship-to, so there is nothing left to ask
        var same = await Service.CreateAsync(request with { UpdateCustomerShipTo = null }, "kim", None);
        same.ShipName.Should().Be("Ship " + m.Key);
    }

    [DatabaseFact, Trait("Id", "SD-UT-133")]
    public async Task AUsInvoice_KeepsTheTaxAreaThroughItsTaxCode_AndTheCertificateItWasExemptedBy()
    {
        var m = await MastersAsync(ShipThenBill);
        var area = await SalesTaxCodeAsync(m.Key);
        var exempt = await ExemptTaxCodeAsync();
        await SetCustomerTaxCodeAsync(m.Customer, area);
        var line = Direct(m.Service, 1m, 100m, m.NoTax) with { TaxCodeId = null };

        var taxed = await Service.CreateAsync(Request(m, line), "kim", None);

        (taxed.Lines[0].TaxCodeId, taxed.TaxAmt, taxed.TaxExemptionCertNo).Should().Be((area, 5m, (string?)null));
        (await TaxAreaOfLineAsync(taxed.Lines[0].Id)).Should().Be("A" + m.Key);

        await AddExemptionAsync(m.Customer, "CERT-" + m.Key, "TX");

        var exempted = await Service.CreateAsync(Request(m, line), "kim", None);

        (exempted.Lines[0].TaxCodeId, exempted.TaxAmt, exempted.TaxExemptionCertNo).Should().Be((exempt, 0m, "CERT-" + m.Key));
        (await Service.GetAsync(exempted.Id, None)).TaxExemptionCertNo.Should().Be("CERT-" + m.Key);
    }

    [DatabaseFact, Trait("Id", "BC-UT-547"), Trait("Id", "SD-UT-132")]
    public async Task AnInvoice_TaxesTheOrderLineAtTheRateOfItsOwnDate()
    {
        var m = await MastersAsync(BillThenShip);
        await ReceiveAsync(m.Item, m.Main, 2m);
        var so = await OrderAsync(m, BillThenShip, 2m, 100m, await SalesTaxCodeAsync(m.Key));

        var before = await Service.CreateAsync(Request(m, FromOrder(so.Line, 1m)), "kim", None);
        var after = await Service.CreateAsync(Request(m, FromOrder(so.Line, 1m)) with { DnDt = AfterTheRateChange }, "kim", None);

        (before.SupplyAmt, before.TaxAmt, before.TotDnAmt).Should().Be((100m, 5m, 105m));
        (after.SupplyAmt, after.TaxAmt, after.TotDnAmt).Should().Be((100m, 8m, 108m));
    }

    [DatabaseFact, Trait("Id", "BC-UT-546")]
    public async Task ACancellationCreditMemo_KeepsTheRateOfTheInvoice_AfterTheRateChanged()
    {
        var m = await MastersAsync(BillThenShip);
        await ReceiveAsync(m.Item, m.Main, 2m);
        var so = await OrderAsync(m, BillThenShip, 2m, 100m, await SalesTaxCodeAsync(m.Key));
        var inv = await Service.CreateAsync(Request(m, FromOrder(so.Line, 2m)), "kim", None);

        var cm = await Service.CancelUnshippedAsync(
            inv.Id, new CancelUnshippedRequest(AfterTheRateChange, null, [new(inv.Lines[0].Id, 2m)], inv.UpdatedAt), "kim", None);

        inv.TotDnAmt.Should().Be(210m);
        (await CreditMemoAsync(cm.CreditMemoId)).TotDnAmt.Should().Be(-210m, "the invoice's 5%, not the 8% of the Credit Memo date");
    }

    [DatabaseFact, Trait("Id", "SD-UC-003-T05"), Trait("Id", "SD-UT-115"), Trait("Id", "SD-UT-131"), Trait("Id", "SD-UT-109")]
    public async Task TwoUsdShipments_AreBilledOnOneInvoice_InTheirCurrency_WithTheBalanceEqualToTheTotal()
    {
        var m = await MastersAsync(ShipThenBill);
        await ReceiveAsync(m.Item, m.Main, 10m);
        var first = await Shipments.CreateAsync(ShipmentRequest(m, ShipmentLine(m.Item, 2m)), "kim", None);
        var second = await Shipments.CreateAsync(ShipmentRequest(m, ShipmentLine(m.Item, 1m)), "kim", None);

        // The request's currency is ignored: the shipments give it (SD-BR-131)
        var inv = await Service.CreateAsync(
            Request(m, FromShipment(first.Lines[0].Id, 2m), FromShipment(second.Lines[0].Id, 1m)) with { CurrencyCode = "KRW", ExchangeRate = 1300m },
            "kim", None);

        (inv.CurrencyCode, inv.ExchangeRate).Should().Be(("USD", 1m));
        inv.Lines.Select(l => (l.ShipNo, l.DnQty)).Should().Equal((first.ShipNo, 2m), (second.ShipNo, 1m));
        (inv.SupplyAmt, inv.TaxAmt, inv.TotDnAmt, inv.BalAmt, inv.TotBaseAmt).Should().Be((300m, 30m, 330m, 330m, 330m));
        (await ShipmentLineAsync(first.Lines[0].Id)).Should().Be((2m, "BL"));
        (await ShipmentLineAsync(second.Lines[0].Id)).Should().Be((1m, "BL"));
    }

    [DatabaseFact, Trait("Id", "SD-UT-142"), Trait("Id", "SD-UT-143"), Trait("Rule", "SD-BR-175"), Trait("Rule", "SD-BR-176"), Trait("Rule", "SD-BR-177")]
    public async Task BillFirstInvoice_ShippedByTheShipmentApi_ThenTheShipmentVoid_GivesTheShippedQtyAndConsignmentStockBack()
    {
        var m = await MastersAsync(BillThenShip);
        await ReceiveAsync(m.Item, m.Main, 6m);
        var so = await OrderAsync(m, BillThenShip, 6m, 100m);
        var inv = await Service.CreateAsync(Request(m, FromOrder(so.Line, 6m)), "kim", None);

        var sh = await Shipments.CreateAsync(ShipmentRequest(m, ShipmentLine(m.Item, 4m) with { SalesInvoiceLineId = inv.Lines[0].Id }), "kim", None);

        (sh.BillingFlow, sh.Lines[0].WarehouseId).Should().Be((BillThenShip, m.Consignment));
        (await OnHandAsync(m.Item, m.Consignment)).Should().Be(2m);
        (await Service.GetAsync(inv.Id, None)).Lines[0].Should().Match<SalesInvoiceLineDto>(l => l.ShippedQty == 4m && l.UnshippedQty == 2m);

        await Shipments.VoidAsync(sh.Id, sh.UpdatedAt, "kim", None);

        (await OnHandAsync(m.Item, m.Consignment)).Should().Be(6m);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(0m);
        var after = await Service.GetAsync(inv.Id, None);
        after.Lines[0].Should().Match<SalesInvoiceLineDto>(l => l.ShippedQty == 0m && l.UnshippedQty == 6m);
        (after.Status, after.TotDnAmt).Should().Be(("OP", 600m), "the shipment does not bill (SD-BR-176)");
    }

    [DatabaseFact, Trait("Id", "SD-UC-003-T07"), Trait("Id", "SD-UT-134")]
    public async Task TaxInvoice_IsNotFoundOutsideKr_PrintsForABusinessCustomer_AndIsRefusedForAnIndividual()
    {
        var business = await MastersAsync(ShipThenBill);
        var individual = await MastersAsync(ShipThenBill);
        await ExecuteAsync(
            "UPDATE bc_customer SET tax_party_type = 'BUSINESS', business_reg_no = '1234567890', ceo_name = 'Demo Ceo' WHERE id = @Id", business.Customer);
        await ExecuteAsync("UPDATE bc_customer SET tax_party_type = 'INDIVIDUAL' WHERE id = @Id", individual.Customer);
        await ExecuteAsync(
            "UPDATE bc_business_place SET business_reg_no = '9876543210', ceo_name = 'Demo Owner' WHERE is_default AND id > @Id", 0);
        var toBusiness = await Service.CreateAsync(Request(business, Direct(business.Service, 1m, 100m, business.Vat10)), "kim", None);
        var toIndividual = await Service.CreateAsync(Request(individual, Direct(individual.Service, 1m, 100m, individual.Vat10)), "kim", None);

        var outsideKr = () => Print.TaxInvoicePdfAsync(toBusiness.Id, CultureInfo.InvariantCulture, None);
        await outsideKr.Should().ThrowAsync<NotFoundException>("a US company has no tax invoice");

        await ExecuteAsync(
            """
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
            SELECT 'Demo Co', 'KR', 'KRW', 'ko', 'Asia/Seoul', 't', 't' WHERE @Id > 0
            """, 1);
        try
        {
            var file = await Print.TaxInvoicePdfAsync(toBusiness.Id, CultureInfo.InvariantCulture, None);
            var refused = () => Print.TaxInvoicePdfAsync(toIndividual.Id, CultureInfo.InvariantCulture, None);

            (file.FileName, file.ContentType).Should().Be((toBusiness.DnNo + "-tax-invoice.pdf", SalesInvoicePrintService.PdfType));
            (await refused.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SalesInvoiceRules.TaxInvoiceCode);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM bc_company WHERE country_code = 'KR' AND id > @Id", 0);
        }
    }

    private SalesInvoicePrintService Print => new(
        Service, db.Get<ISalesInvoiceRepository>(), db.Get<ICustomerRepository>(), db.Get<ICompanyRepository>(), db.Get<IBusinessPlaceRepository>(),
        db.Get<ILocalizationRepository>(), new FakeDocument(), db.Get<IUnitOfWorkFactory>());

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

    // A US sales tax code of a TX tax area: 5% on the invoice date, 8% from the day after it (A + key, T + key)
    private async Task<long> SalesTaxCodeAsync(string key)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.QuerySingleAsync<long>(
            """
            WITH a AS (INSERT INTO bc_tax_area (tax_area_code, tax_area_name, state_code, created_by, updated_by)
                       VALUES ('A' || @Key, 'Area ' || @Key, 'TX', 't', 't') RETURNING id),
                 r AS (INSERT INTO bc_tax_area_rate (tax_area_id, effective_from, rate, created_by, updated_by)
                       SELECT id, d, rate, 't', 't' FROM a, (VALUES ('2026-01-01'::date, 5), ('2026-11-05'::date, 8)) AS v (d, rate))
            INSERT INTO bc_tax_code (tax_code, tax_kind, tax_area_id, created_by, updated_by)
            SELECT 'T' || @Key, 'SALES_TAX', id, 't', 't' FROM a RETURNING id
            """,
            new { Key = key });
    }

    // The one exempt tax code the service finds by its kind; test classes have their own schema, and this class keeps it
    private async Task<long> ExemptTaxCodeAsync()
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.QuerySingleAsync<long>(
            """
            INSERT INTO bc_tax_code (tax_code, tax_kind, created_by, updated_by) VALUES ('EXEMPT_TEST', 'SALES_TAX_EXEMPT', 't', 't')
            ON CONFLICT (tax_code) DO UPDATE SET use_yn = true RETURNING id
            """);
    }

    private async Task SetCustomerTaxCodeAsync(long customer, long taxCode)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync("UPDATE bc_customer SET tax_code_id = @TaxCode WHERE id = @Customer", new { TaxCode = taxCode, Customer = customer });
    }

    private async Task AddExemptionAsync(long customer, string certificateNo, string state)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO sd_customer_tax_exemption (customer_id, certificate_no, state_code, created_by, updated_by)
            VALUES (@Customer, @CertificateNo, @State, 't', 't')
            """,
            new { Customer = customer, CertificateNo = certificateNo, State = state });
    }

    private async Task<string?> TaxAreaOfLineAsync(long line) =>
        (await QueryAsync<string?>(
            """
            SELECT a.tax_area_code FROM sd_sales_invoice_line l
            JOIN bc_tax_code c ON c.id = l.tax_code_id JOIN bc_tax_area a ON a.id = c.tax_area_id
            WHERE l.id = @Id
            """, new { Id = line }))
        .Single();

    private async Task<string?> CustomerShipNameAsync(long customer) =>
        (await QueryAsync<string?>("SELECT ship_name FROM bc_customer WHERE id = @Id", new { Id = customer })).Single();

    // A USD order of the customer with one line of the item, without discount and, unless a tax code is given, without tax
    private async Task<Order> OrderAsync(Masters m, string billingFlow, decimal qty, decimal price, long? taxCode = null)
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
            new { SoNo = soNo, m.Customer, SoDt = DnDt, Flow = billingFlow, m.Item, Qty = qty, Price = price, NoTax = taxCode ?? m.NoTax });
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

    // The PDF itself is tested in SalesInvoiceDocumentTests; here only whether it is printed
    private sealed class FakeDocument : ISalesInvoiceDocument
    {
        public Task<byte[]> SalesInvoicePdfAsync(SalesInvoicePrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]>([1]);

        public Task<byte[]> TaxInvoicePdfAsync(TaxInvoicePrint print, PrintPaper paper, CultureInfo culture, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]>([1]);
    }

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

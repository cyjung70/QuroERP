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
using QuroErp.Application.Mm.Labels;
using QuroErp.Application.Mm.Pallets;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.GoodsReceipts;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Domain.Mm.Lots;
using QuroErp.Domain.Mm.PurchaseOrders;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.GoodsReceipts;

/// <summary>Own schema for goods receipt tests: the services commit, so their rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class GoodsReceiptSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "GoodsReceiptSchema";
}

/// <summary>
/// GoodsReceiptService with the Dapper repositories and the inventory service on real PostgreSQL (MM-UC-004). Without a company
/// the base currency is USD. Today is <see cref="Today"/>; each test makes its own vendor and items and sets the inventory modes
/// it needs.
/// </summary>
[Collection(GoodsReceiptSchemaDefinition.Name)]
public partial class GoodsReceiptServiceTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly GrDt = new(2026, 11, 4);
    private static readonly DateOnly Today = new(2026, 11, 10);

    private GoodsReceiptService Service => new(
        db.Get<IGoodsReceiptRepository>(), db.Get<ITaxRepository>(), new TaxAreaRateProvider(db.Get<ITaxRepository>()),
        db.Get<ILocalizationRepository>(), db.Get<IExchangeRateRepository>(), db.Get<IBusinessPlaceRepository>(),
        db.Get<IWarehouseRepository>(), db.Get<IPalletRepository>(), Inventory, new DocumentNumberService(db.Get<IDocumentNumberRepository>()),
        new FixedClock(), db.Get<IUnitOfWorkFactory>());

    private PurchaseOrderService Orders => new(
        db.Get<IPurchaseOrderRepository>(), db.Get<ITaxRepository>(), new TaxAreaRateProvider(db.Get<ITaxRepository>()),
        db.Get<ILocalizationRepository>(), db.Get<IBusinessPlaceRepository>(), new DocumentNumberService(db.Get<IDocumentNumberRepository>()),
        db.Get<IWarehouseRepository>(), db.Get<IUnitOfWorkFactory>());

    private InventoryService Inventory => new(
        db.Get<IInventoryRepository>(), db.Get<IInventorySettingRepository>(), db.Get<ISetComponentRepository>(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    [DatabaseFact, Trait("Id", "MM-UC-004-T01"), Trait("Id", "MM-UT-108"), Trait("Id", "MM-UT-109"), Trait("Id", "MM-UT-110"),
     Trait("Id", "MM-UT-111"), Trait("Id", "MM-UT-113")]
    public async Task Create_FromAPo_NumbersReceivesStock_AndUpdatesThePo()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var po = await ApprovedPoAsync(m, (m.Item, 10m, 100m, m.Vat10), (m.Service, 2m, 5m, m.NoTax));

        var gr = await Service.CreateAsync(
            Request(m, PoLine(po.Lines[0], 4m, m.Vat10), PoLine(po.Lines[1], 2m, m.NoTax) with { ItemId = m.Service }), "kim", None);

        GrNumber().IsMatch(gr.GrNo).Should().BeTrue(gr.GrNo);
        (gr.Status, gr.SupplyAmt, gr.TaxAmt, gr.TotGrAmt, gr.TotBaseAmt, gr.CurrencyCode, gr.WarehouseId)
            .Should().Be((GoodsReceiptStatus.Open, 410m, 40m, 450m, 450m, "USD", m.Main));
        gr.Lines.Select(l => (l.Seq, l.PoNo, l.PoSeq, l.PoReceivedQty)).Should().Equal((1, po.PoNo, 1, 4m), (2, po.PoNo, 2, 2m));
        (await Orders.GetAsync(po.Id, None)).Status.Should().Be(PoStatusCode.PartiallyReceived);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(4m);
        (await OnHandAsync(m.Service, m.Main)).Should().Be(0m, "service items have no stock");
        (await MovementsAsync(gr.GrNo)).Should().Equal(("GR", 1, 4m, 400m, (DateOnly?)null));
        (await StandardPriceAsync(m.Item)).Should().Be(100m);
        (await Service.OpenPoLinesAsync(m.Vendor, None)).Select(l => (l.PoSeq, l.RemainingQty, l.WarehouseId))
            .Should().Equal((1, 6m, (long?)m.Main));
        (await Service.SearchAsync(new(gr.GrNo[..6], m.Vendor, GrDt, GrDt), None)).Should().ContainSingle(s => s.Id == gr.Id && s.Status == "OP");
    }

    [DatabaseFact, Trait("Id", "MM-UC-004-T02"), Trait("Id", "MM-UT-601"), Trait("Id", "MM-UT-018"), Trait("Id", "MM-UT-114")]
    public async Task Update_AddsAndDeletesLines_TakingBackStockAndPoQty()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var po = await ApprovedPoAsync(m, (m.Item, 5m, 100m, m.Vat10));
        var gr = await Service.CreateAsync(Request(m, PoLine(po.Lines[0], 5m, m.Vat10), Direct(m.Item, 3m)), "kim", None);
        (await Orders.GetAsync(po.Id, None)).Status.Should().Be(PoStatusCode.Received);

        var saved = await Service.UpdateAsync(gr.Id, Request(m, gr.UpdatedAt, Keep(gr.Lines[1]), Direct(m.Item, 2m)) with { RefNo = "dn-2" }, "kim", None);

        saved.Lines.Select(l => (l.Seq, l.GrQty)).Should().Equal((2, 3m), (3, 2m));
        saved.RefNo.Should().Be("DN-2");
        (await OnHandAsync(m.Item, m.Main)).Should().Be(5m);
        var po2 = await Orders.GetAsync(po.Id, None);
        (po2.Status, po2.ReceivedQty).Should().Be((PoStatusCode.Approved, 0m));
        (await MovementsAsync(gr.GrNo)).Should().Equal(
            ("GR", 1, 5m, 500m, null), ("GR", 2, 3m, 300m, null), ("GR", 1, -5m, -500m, Today), ("GR", 3, 2m, 200m, null));
    }

    [DatabaseFact, Trait("Id", "MM-UT-107"), Trait("Id", "MM-UT-116"), Trait("Id", "MM-UT-117")]
    public async Task Update_RefusesAChangedLine_AnotherVendor_AndANewDateWhileLinesExist()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var other = await MastersAsync();
        var gr = await Service.CreateAsync(Request(m, Direct(m.Item, 3m)), "kim", None);

        var changed = () => Service.UpdateAsync(gr.Id, Request(m, gr.UpdatedAt, Keep(gr.Lines[0]) with { GrQty = 4m }), "kim", None);
        var vendor = () => Service.UpdateAsync(gr.Id, Request(m, gr.UpdatedAt, Keep(gr.Lines[0])) with { VendorId = other.Vendor }, "kim", None);
        var date = () => Service.UpdateAsync(gr.Id, Request(m, gr.UpdatedAt, Keep(gr.Lines[0])) with { GrDt = GrDt.AddDays(1) }, "kim", None);

        (await changed.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(GoodsReceiptRules.LineChangedCode);
        (await vendor.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(GoodsReceiptRules.VendorChangedCode);
        (await date.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(GoodsReceiptRules.DateChangedCode);
    }

    [DatabaseFact, Trait("Id", "MM-UC-004-T03")]
    public async Task AFailingLine_RollsBackTheWholeSave()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var po = await ApprovedPoAsync(m, (m.Item, 5m, 100m, m.Vat10));

        // The item is not Location-managed, so the stock posting refuses the Location after the header and lines are written
        var act = () => Service.CreateAsync(
            Request(m, PoLine(po.Lines[0], 5m, m.Vat10), Direct(m.Item, 1m) with { LocationId = m.Shelf, PalletQty = 1m }), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(StockKeyRules.LocationCode);
        (await CountAsync("SELECT count(*) FROM mm_pallet WHERE item_id = @Id", m.Item)).Should().Be(0);
        (await CountAsync("SELECT count(*) FROM mm_goods_receipt WHERE vendor_id = @Id", m.Vendor)).Should().Be(0);
        (await Orders.GetAsync(po.Id, None)).Status.Should().Be(PoStatusCode.Approved);
        (await CountAsync("SELECT coalesce(sum(receipt_qty), 0)::int FROM mm_purchase_order_line WHERE purchase_order_id = @Id", po.Id)).Should().Be(0);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(0m);
    }

    [DatabaseFact, Trait("Id", "MM-UC-004-T04")]
    public async Task StaleUpdatedAt_Is409()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var gr = await Service.CreateAsync(Request(m, Direct(m.Item, 3m)), "kim", None);
        await Service.UpdateAsync(gr.Id, Request(m, gr.UpdatedAt, Keep(gr.Lines[0])) with { Remarks = "x" }, "kim", None);

        var update = () => Service.UpdateAsync(gr.Id, Request(m, gr.UpdatedAt, Keep(gr.Lines[0])), "kim", None);
        var delete = () => Service.DeleteAsync(gr.Id, gr.UpdatedAt, "kim", None);

        await update.Should().ThrowAsync<ConcurrencyException>();
        await delete.Should().ThrowAsync<ConcurrencyException>();
    }

    [DatabaseFact, Trait("Id", "MM-UT-104"), Trait("Id", "MM-UT-790")]
    public async Task InvoicedOrReturnedLines_LockTheReceipt()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var gr = await Service.CreateAsync(Request(m, Direct(m.Item, 3m)), "kim", None);
        await ExecuteAsync("UPDATE mm_goods_receipt_line SET invoiced_qty = 1 WHERE goods_receipt_id = @Id", gr.Id);

        var update = () => Service.UpdateAsync(gr.Id, Request(m, gr.UpdatedAt, Keep(gr.Lines[0])) with { Remarks = "x" }, "kim", None);
        var add = () => Service.UpdateAsync(gr.Id, Request(m, gr.UpdatedAt, Keep(gr.Lines[0]), Direct(m.Item, 1m)), "kim", None);
        var delete = () => Service.DeleteAsync(gr.Id, gr.UpdatedAt, "kim", None);

        (await update.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(GoodsReceiptRules.LockedCode);
        (await add.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(GoodsReceiptRules.LockedCode);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(GoodsReceiptRules.LockedCode);
        (await Service.GetAsync(gr.Id, None)).Status.Should().Be(GoodsReceiptStatus.PartiallyInvoiced);
    }

    [DatabaseFact, Trait("Id", "MM-UT-102"), Trait("Id", "MM-TS-002")]
    public async Task OverReceipt_IsRefusedInDeny_AndAskedInWarn()
    {
        var m = await MastersAsync();
        var po = await ApprovedPoAsync(m, (m.Item, 5m, 100m, m.Vat10));
        var request = Request(m, PoLine(po.Lines[0], 6m, m.Vat10));

        await ModesAsync("DENY", "DENY");
        var deny = () => Service.CreateAsync(request, "kim", None);
        (await deny.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(po.PoNo, 1, 5m);

        await ModesAsync("DENY", "WARN");
        var warn = () => Service.CreateAsync(request, "kim", None);
        var asked = (await warn.Should().ThrowAsync<ConfirmationRequiredException>()).Which;
        var saved = await Service.CreateAsync(request with { ConfirmOverReceipt = true }, "kim", None);

        (asked.Code, asked.ConfirmField).Should().Be((GoodsReceiptRules.OverReceiptWarningCode, GoodsReceiptRules.ConfirmOverReceiptField));
        saved.Lines.Single().PoReceivedQty.Should().Be(6m);
        (await Orders.GetAsync(po.Id, None)).Status.Should().Be(PoStatusCode.Received);
    }

    [DatabaseFact, Trait("Id", "MM-UC-004-T06"), Trait("Id", "MM-UC-038-T06"), Trait("Id", "MM-UT-353"), Trait("Id", "MM-UT-356")]
    public async Task LotItems_GetANewLot_AndALotBalanceBelowZeroIsRefusedEvenInAllowInMonth()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var first = await Service.CreateAsync(Request(m, Direct(m.LotItem, 5m)), "kim", None);
        var second = await Service.CreateAsync(Request(m, Direct(m.LotItem, 2m), Direct(m.Item, 1m)), "kim", None);
        await PostAsync(new StockMovementRequest("DN", GrDt.AddDays(1), m.LotItem, m.Main, -4m, LotNo: first.Lines[0].LotNo));
        // Enough stock of the item in another lot: only the first lot would go below zero
        await Service.CreateAsync(Request(m, Direct(m.LotItem, 10m)), "kim", None);

        await ModesAsync("ALLOW_IN_MONTH", "DENY");
        var delete = () => Service.DeleteAsync(first.Id, first.UpdatedAt, "kim", None);

        (first.Lines.Single().LotNo, second.Lines[0].LotNo, second.Lines[1].LotNo).Should().Be(("261104-001", "261104-002", "*"));
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LotRules.NegativeCode);
    }

    [DatabaseFact, Trait("Id", "MM-UT-364"), Trait("Id", "MM-UT-367"), Trait("Id", "MM-UT-104")]
    public async Task LotAttributes_AreSavedOnTheNewLot_AndCanChangeAfterInvoicing()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var gr = await Service.CreateAsync(
            Request(m, Direct(m.LotItem, 5m) with { SupplierLotNo = " s-77 ", MfgDt = GrDt.AddDays(-30), ExpiryDt = GrDt.AddDays(300) }),
            "kim", None);
        var line = gr.Lines.Single();
        await ExecuteAsync("UPDATE mm_goods_receipt_line SET invoiced_qty = 1 WHERE goods_receipt_id = @Id", gr.Id);

        var saved = await Service.UpdateAsync(
            gr.Id, Request(m, gr.UpdatedAt, Keep(line) with { SupplierLotNo = "S-78", MfgDt = null }), "kim", None);
        var wrong = () => Service.UpdateAsync(
            gr.Id, Request(m, saved.UpdatedAt, Keep(saved.Lines[0]) with { MfgDt = GrDt, ExpiryDt = GrDt.AddDays(-1) }), "kim", None);

        (line.SupplierLotNo, line.MfgDt, line.ExpiryDt).Should().Be(("s-77", GrDt.AddDays(-30), GrDt.AddDays(300)));
        (await CountAsync("SELECT count(*) FROM mm_lot WHERE goods_receipt_line_id = @Id", line.Id)).Should().Be(1);
        var after = saved.Lines.Single();
        (after.SupplierLotNo, after.MfgDt, after.ExpiryDt, after.LotNo).Should().Be(("S-78", (DateOnly?)null, GrDt.AddDays(300), line.LotNo));
        saved.UpdatedAt.Should().BeAfter(gr.UpdatedAt);
        (await wrong.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LotRules.AttributesCode);
    }

    [DatabaseFact, Trait("Id", "MM-UT-910"), Trait("Id", "MM-UT-916"), Trait("Id", "MM-UT-713")]
    public async Task Pallets_SplitTheLineQty_OnItsLocationAndLot()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();

        var gr = await Service.CreateAsync(
            Request(m, Direct(m.LocLotItem, 25m) with { LocationId = m.Shelf, PalletQty = 10m }, Direct(m.Item, 3m)), "kim", None);

        var line = gr.Lines[0];
        (line.LocationId, line.LocationCode, line.PalletQty).Should().Be((m.Shelf, "A1", 10m));
        gr.Lines[1].LocationCode.Should().Be("*");
        gr.Lines[1].PalletNos.Should().BeEmpty();
        line.PalletNos.Should().HaveCount(3).And.OnlyContain(no => PalletNumber().IsMatch(no));
        (await PalletsAsync(line.Id)).Should().Equal(
            (line.PalletNos![0], 10m, "ACTIVE", m.Shelf, line.LotId!.Value, "CREATE:10"),
            (line.PalletNos[1], 10m, "ACTIVE", m.Shelf, line.LotId!.Value, "CREATE:10"),
            (line.PalletNos[2], 5m, "ACTIVE", m.Shelf, line.LotId!.Value, "CREATE:5"));
        (await OnHandAsync(m.LocLotItem, m.Main)).Should().Be(25m);
    }

    [DatabaseFact, Trait("Id", "MM-UT-911"), Trait("Id", "MM-UT-916")]
    public async Task DeletingALine_ClosesItsPallets_UnlessOneWasMoved()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var gr = await Service.CreateAsync(
            Request(m, Direct(m.LotItem, 6m) with { PalletQty = 4m }, Direct(m.Item, 5m) with { PalletQty = 5m }), "kim", None);
        var moved = await Service.CreateAsync(Request(m, Direct(m.Item, 2m) with { PalletQty = 2m }), "kim", None);
        await ExecuteAsync(
            """
            INSERT INTO mm_pallet_event (pallet_id, event_type, event_dt, qty, created_by, updated_by)
            SELECT id, 'MOVE', '2026-11-05', qty, 't', 't' FROM mm_pallet WHERE goods_receipt_line_id = @Id
            """,
            moved.Lines[0].Id);

        var saved = await Service.UpdateAsync(gr.Id, Request(m, gr.UpdatedAt, Keep(gr.Lines[1])), "kim", None);
        await Service.DeleteAsync(saved.Id, saved.UpdatedAt, "kim", None);
        var refused = () => Service.DeleteAsync(moved.Id, moved.UpdatedAt, "kim", None);

        var (lotPallets, lotId) = (gr.Lines[0].PalletNos!, gr.Lines[0].LotId!.Value);
        (await PalletsAsync(null, lotPallets)).Should().Equal(
            (lotPallets[0], 0m, "CLOSED", m.StarMain, lotId, "CREATE:4,ADJUST:-4"),
            (lotPallets[1], 0m, "CLOSED", m.StarMain, lotId, "CREATE:2,ADJUST:-2"));
        (await PalletsAsync(null, gr.Lines[1].PalletNos)).Should().Equal(
            (gr.Lines[1].PalletNos![0], 0m, "CLOSED", m.StarMain, gr.Lines[1].LotId!.Value, "CREATE:5,ADJUST:-5"));
        (await CountAsync("SELECT count(*) FROM mm_lot WHERE id = @Id AND goods_receipt_line_id IS NULL", lotId)).Should().Be(1);
        var error = (await refused.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args).Should().BeEquivalentTo((PalletRules.ChangedCode, new object[] { moved.Lines[0].PalletNos![0] }));
        (await OnHandAsync(m.Item, m.Main)).Should().Be(2m);
    }

    [DatabaseFact, Trait("Id", "MM-UT-918"), Trait("Id", "MM-UT-919")]
    public async Task PalletLabels_PrintTheActivePalletsAsPdf_AndKeepTheHistory()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var gr = await Service.CreateAsync(
            Request(m, Direct(m.LotItem, 3m) with { PalletQty = 2m, ExpiryDt = new(2027, 5, 31) }, Direct(m.Item, 1m)), "kim", None);
        var none = await Service.CreateAsync(Request(m, Direct(m.Item, 1m)), "kim", None);
        var pdf = new FakePdf();
        var labels = new GoodsReceiptLabelService(
            db.Get<IGoodsReceiptRepository>(), db.Get<IPalletRepository>(), db.Get<ILabelFormatRepository>(),
            new LabelPrintService(pdf, db.Get<ILabelPrintRepository>(), db.Get<IUnitOfWorkFactory>()), db.Get<IUnitOfWorkFactory>());

        var output = await labels.PrintPalletLabelsAsync(gr.Id, "kim", None);
        var missing = () => labels.PrintPalletLabelsAsync(none.Id, "kim", None);

        (output.OutputKind, output.ContentType).Should().Be((LabelType.Pdf, LabelPrintService.PdfContentType));
        var (pallets, lotNo) = (gr.Lines[0].PalletNos!, gr.Lines[0].LotNo);
        pdf.Pages.Select(p => (p.Label.Code, p.Text, p.Barcode)).Should().Equal(
            ($"PL|{pallets[0]}|L{m.Key}|{lotNo}|2|20270531", true, false),
            ($"PL|{pallets[0]}|L{m.Key}|{lotNo}|2|20270531", false, true),
            ($"PL|{pallets[1]}|L{m.Key}|{lotNo}|1|20270531", true, false),
            ($"PL|{pallets[1]}|L{m.Key}|{lotNo}|1|20270531", false, true));
        pdf.Pages[0].Label.Values["expiryDt"].Should().Be("2027-05-31");
        (await CountAsync(
            """
            SELECT count(*) FROM mm_label_print
            WHERE label_type = 'PALLET' AND output_kind = 'PDF' AND ref_id IN (SELECT id FROM mm_pallet WHERE goods_receipt_line_id = @Id)
            """,
            gr.Lines[0].Id)).Should().Be(2);
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    [DatabaseFact, Trait("Id", "MM-UC-004-T07"), Trait("Id", "MM-UC-038-T07"), Trait("Id", "MM-UT-001"), Trait("Id", "MM-UT-602")]
    public async Task Deny_RefusesADeleteBelowTheWarehouseOnHand()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var gr = await Service.CreateAsync(Request(m, Direct(m.Item, 10m)), "kim", None);
        await PostAsync(new StockMovementRequest("DN", GrDt.AddDays(1), m.Item, m.Main, -8m));

        var delete = () => Service.DeleteAsync(gr.Id, gr.UpdatedAt, "kim", None);

        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(StockPolicy.InsufficientStock);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(2m);
    }

    [DatabaseFact, Trait("Id", "MM-UC-004-T08"), Trait("Id", "MM-UC-038-T08"), Trait("Id", "MM-UT-002")]
    public async Task AllowInMonth_SavesAMidMonthNegative_AndMarksIt()
    {
        await ModesAsync("ALLOW_IN_MONTH", "DENY");
        var m = await MastersAsync();
        var gr = await Service.CreateAsync(Request(m, Direct(m.Item, 10m)), "kim", None);
        await PostAsync(new StockMovementRequest("DN", GrDt.AddDays(1), m.Item, m.Main, -8m));
        await Service.CreateAsync(Request(m, Direct(m.Item, 10m)) with { GrDt = new(2026, 11, 20) }, "kim", None);

        await Service.DeleteAsync(gr.Id, gr.UpdatedAt, "kim", None);

        (await CountAsync(
            "SELECT count(*) FROM mm_goods_movement WHERE ref_key = (SELECT @No) AND parent_movement_id IS NOT NULL AND negative_stock",
            gr.GrNo)).Should().Be(1);
        (await OnHandAsync(m.Item, m.Main)).Should().Be(2m);
    }

    [DatabaseFact, Trait("Id", "MM-UC-004-T09"), Trait("Id", "MM-UC-038-T09"), Trait("Id", "MM-UT-009")]
    public async Task Deny_RefusesADeleteThatMakesALaterDateNegative()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        var gr = await Service.CreateAsync(Request(m, Direct(m.Item, 10m)), "kim", None);
        await PostAsync(new StockMovementRequest("DN", new(2026, 11, 20), m.Item, m.Main, -10m));
        await Service.CreateAsync(Request(m, Direct(m.Item, 10m)) with { GrDt = new(2026, 11, 25) }, "kim", None);

        var delete = () => Service.DeleteAsync(gr.Id, gr.UpdatedAt, "kim", None);

        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PointInTimeStock.NegativeOnDate);
    }

    [DatabaseFact, Trait("Id", "MM-UC-004-T10"), Trait("Id", "MM-UC-038-T10"), Trait("Id", "MM-UT-010")]
    public async Task AllowInMonth_RefusesANegativeMonthEnd_WithNoMovementOnTheLastDay()
    {
        await ModesAsync("ALLOW_IN_MONTH", "DENY");
        var m = await MastersAsync();
        var gr = await Service.CreateAsync(Request(m, Direct(m.Item, 10m)), "kim", None);
        await PostAsync(new StockMovementRequest("DN", GrDt.AddDays(1), m.Item, m.Main, -8m));

        var delete = () => Service.DeleteAsync(gr.Id, gr.UpdatedAt, "kim", None);

        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(PointInTimeStock.NegativeAtMonthEnd);
    }

    [DatabaseFact, Trait("Id", "MM-UT-703"), Trait("Id", "MM-UT-710")]
    public async Task Warehouses_DefaultFromTheHeader_AndTheConsignmentWarehouseIsRefused()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();

        var gr = await Service.CreateAsync(Request(m, Direct(m.Item, 1m) with { WarehouseId = m.Second }, Direct(m.Item, 2m)), "kim", None);
        var consignment = () => Service.CreateAsync(Request(m, Direct(m.Item, 1m) with { WarehouseId = m.Consignment }), "kim", None);

        gr.Lines.Select(l => l.WarehouseId).Should().Equal(m.Second, m.Main);
        (await OnHandAsync(m.Item, m.Second)).Should().Be(1m);
        (await consignment.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-710");
    }

    [DatabaseFact, Trait("Id", "BC-UT-425"), Trait("Rule", "BC-BR-425")]
    public async Task ReceiptLines_CountForTheirDocumentCurrency()
    {
        await ModesAsync("DENY", "DENY");
        var m = await MastersAsync();
        await Service.CreateAsync(Request(m, Direct(m.Item, 1m)), "kim", None);

        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        (await db.Get<ILocalizationSettingRepository>().FindDocumentCurrenciesAsync(DecimalModule.Mm, uow.Transaction, None))
            .Should().Contain("USD");
    }

    private static SaveGoodsReceiptRequest Request(Masters m, params SaveGoodsReceiptLineRequest[] lines) => Request(m, null, lines);

    private static SaveGoodsReceiptRequest Request(Masters m, DateTimeOffset? updatedAt, params SaveGoodsReceiptLineRequest[] lines) =>
        new(m.Vendor, GrDt, m.Main, "dn-1", null, null, null, lines, updatedAt);

    private static SaveGoodsReceiptLineRequest Direct(long item, decimal qty, decimal price = 100m) =>
        new(null, item, null, null, qty, price, null);

    private static SaveGoodsReceiptLineRequest PoLine(PurchaseOrderLineDto line, decimal qty, long taxCode) =>
        new(null, line.ItemId, null, line.WarehouseId, qty, line.PoPrc, taxCode, PurchaseOrderLineId: line.Id);

    private static SaveGoodsReceiptLineRequest Keep(GoodsReceiptLineDto l) =>
        new(l.Id, l.ItemId, l.ItemName, l.WarehouseId, l.GrQty, l.GrPrc, l.TaxCodeId, l.Deductible, l.PurchaseOrderLineId, l.LocationId,
            l.SupplierLotNo, l.MfgDt, l.ExpiryDt, l.PalletQty);

    private async Task<PurchaseOrderDto> ApprovedPoAsync(Masters m, params (long Item, decimal Qty, decimal Price, long TaxCode)[] lines)
    {
        var draft = await Orders.CreateAsync(new SavePurchaseOrderRequest(
            m.Vendor, GrDt, "USD", null, null, null, m.Main, null, null, null,
            [.. lines.Select(l => new SavePurchaseOrderLineRequest(null, l.Item, null, l.Qty, l.Price, 0m, null, null, l.TaxCode))], null,
            UpdateVendorPrice: false), "kim", None);
        return await Orders.ApproveAsync(draft.Id, draft.UpdatedAt, "kim", None);
    }

    private sealed record Masters(
        long Vendor, long Item, long Service, long LotItem, long Main, long Second, long Consignment, long Vat10, long NoTax, long LocLotItem,
        long Shelf, long StarMain, string Key);

    // A USD vendor whose default tax code is a 10% VAT code, an inventory, a service, a lot-managed and a Location- and
    // lot-managed item, the warehouses, and Locations A1 and '*' of the main warehouse
    private async Task<Masters> MastersAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var ids = await connection.QuerySingleAsync<(long Vat10, long NoTax, long Vendor, long Item, long Service, long LotItem, long Main, long Second, long Consignment, long LocLotItem)>(
            """
            WITH t AS (INSERT INTO bc_tax_code (tax_code, tax_kind, rate, created_by, updated_by)
                       VALUES ('V' || @Key, 'VAT_STANDARD', 10, 't', 't') RETURNING id),
                 v AS (INSERT INTO bc_vendor (v_name, currency_code, tax_code_id, created_by, updated_by)
                       SELECT 'Vendor ' || @Key, 'USD', id, 't', 't' FROM t RETURNING id),
                 i AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('I' || @Key, 'Bolt', 'INVENTORY', 't', 't') RETURNING id),
                 s AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('S' || @Key, 'Freight', 'SERVICE', 't', 't') RETURNING id),
                 l AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, created_by, updated_by)
                       VALUES ('L' || @Key, 'Paint', 'INVENTORY', true, '2026-10-01', 't', 't') RETURNING id),
                 w AS (INSERT INTO bc_warehouse (warehouse_code, warehouse_name, business_place_id, created_by, updated_by)
                       SELECT 'W' || @Key, 'Second', id, 't', 't' FROM bc_business_place WHERE is_default RETURNING id),
                 ll AS (INSERT INTO bc_item (item_cd, item_name, type, lot_managed, lot_managed_from, location_managed, created_by, updated_by)
                        VALUES ('K' || @Key, 'Glue', 'INVENTORY', true, '2026-10-01', true, 't', 't') RETURNING id)
            SELECT (SELECT id FROM t), (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), (SELECT id FROM v), (SELECT id FROM i),
                   (SELECT id FROM s), (SELECT id FROM l), (SELECT id FROM bc_warehouse WHERE is_default), (SELECT id FROM w),
                   (SELECT id FROM bc_warehouse WHERE warehouse_type = 'CONSIGNMENT'), (SELECT id FROM ll)
            """,
            new { Key = key });
        // Location A1 of the main warehouse is shared by the tests; '*' may already be there
        var (shelf, star) = await connection.QuerySingleAsync<(long, long)>(
            """
            INSERT INTO bc_location (warehouse_id, location_code, created_by, updated_by)
            VALUES (@Main, 'A1', 't', 't') ON CONFLICT (warehouse_id, location_code) DO NOTHING;
            INSERT INTO bc_location (warehouse_id, location_code, is_system, created_by, updated_by)
            VALUES (@Main, '*', true, 't', 't') ON CONFLICT (warehouse_id, location_code) DO NOTHING;
            SELECT (SELECT id FROM bc_location WHERE warehouse_id = @Main AND location_code = 'A1'),
                   (SELECT id FROM bc_location WHERE warehouse_id = @Main AND location_code = '*');
            """,
            new { ids.Main });
        return new Masters(
            ids.Vendor, ids.Item, ids.Service, ids.LotItem, ids.Main, ids.Second, ids.Consignment, ids.Vat10, ids.NoTax, ids.LocLotItem, shelf,
            star, key);
    }

    private async Task ModesAsync(string negativeStockMode, string overReceiptMode)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(
            "UPDATE mm_inventory_setting SET negative_stock_mode = @Negative, over_receipt_mode = @Over",
            new { Negative = negativeStockMode, Over = overReceiptMode });
    }

    private async Task PostAsync(StockMovementRequest request)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await Inventory.PostAsync([request], "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
    }

    private async Task<decimal> OnHandAsync(long item, long warehouse)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<decimal?>(
            "SELECT sum(onhand_qty) FROM mm_onhand WHERE item_id = @Item AND warehouse_id = @Warehouse", new { Item = item, Warehouse = warehouse }) ?? 0m;
    }

    private async Task<decimal?> StandardPriceAsync(long item)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<decimal?>("SELECT usd_prc FROM bc_item WHERE id = @Item", new { Item = item });
    }

    // Movements of a receipt in id order: type, line, qty, amount and, for a reversal, its date
    private async Task<List<(string MovType, int RefSeq, decimal Qty, decimal Amount, DateOnly? ReversalDt)>> MovementsAsync(string grNo)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return [.. (await connection.QueryAsync<(string, int, decimal, decimal, DateTime?)>(
                """
                SELECT mov_type, ref_seq, qty, amount, CASE WHEN parent_movement_id IS NULL THEN NULL ELSE document_dt END
                FROM mm_goods_movement WHERE ref_key = @GrNo ORDER BY id
                """, new { GrNo = grNo }))
            .Select(r => (r.Item1, r.Item2, r.Item3, r.Item4, r.Item5 is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null))];
    }

    // Pallets of a receipt line, or with the given numbers, in number order: number, qty, status, Location, Lot and the events
    private async Task<List<(string PalletNo, decimal Qty, string Status, long LocationId, long LotId, string Events)>> PalletsAsync(
        long? lineId, IReadOnlyList<string>? numbers = null)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return [.. await connection.QueryAsync<(string, decimal, string, long, long, string)>(
            """
            SELECT p.pallet_no, p.qty, p.status, p.location_id, p.lot_id,
                   (SELECT string_agg(e.event_type || ':' || trim_scale(e.qty)::text, ',' ORDER BY e.id)
                    FROM mm_pallet_event e WHERE e.pallet_id = p.id)
            FROM mm_pallet p
            WHERE p.goods_receipt_line_id = @LineId OR p.pallet_no = ANY(@Numbers)
            ORDER BY p.pallet_no
            """,
            new { LineId = lineId, Numbers = (numbers ?? []).ToArray() })];
    }

    private async Task<int> CountAsync(string sql, object id)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(sql.Replace("@No", "@Id"), new { Id = id });
    }

    private async Task ExecuteAsync(string sql, long id)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => GoodsReceiptServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }

    private sealed class FakePdf : ILabelPdfRenderer
    {
        public List<LabelPage> Pages { get; } = [];

        public Task<byte[]> RenderAsync(LabelFormat format, IReadOnlyList<LabelPage> pages, int copies, CancellationToken cancellationToken)
        {
            Pages.AddRange(pages);
            return Task.FromResult("%PDF"u8.ToArray());
        }
    }

    [GeneratedRegex(@"^GR2611\d{6}$")]
    private static partial Regex GrNumber();

    [GeneratedRegex(@"^PL2611\d{6}$")]
    private static partial Regex PalletNumber();
}

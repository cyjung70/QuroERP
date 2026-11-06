using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quro.Grid.Data;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Contracts.Sd.Customers;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Codes;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.Tax;
using QuroErp.Web.Bc.UserProfiles;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Sd;
using QuroErp.Web.Components.Pages.Sd.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Lots;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Sd.Customers;
using QuroErp.Web.Sd.SalesOrders;
using QuroErp.Web.Sd.Shipments;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Sd;

/// <summary>Shipment screen /sd/shipment (SD-UC-051, TASK-SD-064) against a fake API. The company is US (base USD).</summary>
public class ShipmentScreenTests : BunitContext
{
    private const string Screen = "shipment";
    private readonly FakeShipmentApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public ShipmentScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<ShipmentApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<SalesOrderApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CustomerApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ExchangeRateApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<TaxCodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<UserProfileApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LotApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<FieldPolicyApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<FieldPolicyCache>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "MM-BR-703")]
    public void New_StartsFromTheUsersWarehouse_WithImportAndVoidDisabled_UntilACustomerIsPicked()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=shipment-doc-no]").TextContent.Should().Be("새 문서");
        page.Field("shipment-header-warehouse-code").Value.Should().Be("MAIN");
        page.Find("[data-testid=shipment-header-warehouse-name]").TextContent.Should().Be("Main Store");
        page.Field("shipment-header-currency-code").Value.Should().Be("USD");
        page.Field("shipment-header-currency-code").IsReadOnly.Should().BeTrue();
        Enabled(page).Should().BeEquivalentTo("search", "new", "save", "add-row");
        Button(page, "import-order").HasAttribute("disabled").Should().BeTrue();
        Button(page, "void").HasAttribute("disabled").Should().BeTrue("a blocked command is disabled, not hidden");
        page.Find(".quro-entry-lines-head").TextContent.Should().Contain("거래처를 먼저 선택하세요.");
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "SD-BR-172"), Trait("Rule", "SD-BR-170"), Trait("Rule", "SD-BR-174")]
    public void ImportOrder_AddsTheRemainingLinesFromTheHeaderWarehouse_AndSavePostsThem()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));

        ImportAllOrderLines(page);

        var first = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (first.ItemCd, first.ShipQty, first.ShipPrc, first.WarehouseCode, first.WarehouseId, first.SalesOrderLineId, first.SoNo, first.RemainingQty)
            .Should().Be(("BOLT", 6m, 10m, "MAIN", 1L, 5001L, "SO2611000001", 6m));
        (Frame(page).Header.BillingFlow, Frame(page).Header.ShipVia, Frame(page).Header.ShipName).Should().Be(("SHIP_AND_BILL", "UPS", "ACME DOCK"));

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        var request = _api.Saved[0];
        (request.CustomerId, request.WarehouseId, request.CurrencyCode, request.UpdatedAt).Should().Be((801L, 1L, "USD", (DateTimeOffset?)null));
        request.Lines.Select(l => (l.Id, l.ItemId, l.WarehouseId, l.ShipQty, l.ShipPrc, l.TaxCodeId, l.SalesOrderLineId))
            .Should().Equal(
                ((long?)null, 11L, (long?)1L, 6m, (decimal?)null, (long?)null, (long?)5001L),
                ((long?)null, 12L, (long?)1L, 2m, (decimal?)null, (long?)null, (long?)5002L));
        page.WaitForAssertion(() => page.Find("[data-testid=shipment-doc-no]").TextContent.Should().Be("SH0000001"));
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "SD-BR-170")]
    public void TotalsPanel_ShowsLinesQtyAndAmountWithoutTax_InTheDocumentCurrency_AndNoBaseTotal()
    {
        _api.OrderCurrency = ("EUR", 1.1m);
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));

        ImportAllOrderLines(page);

        page.WaitForAssertion(() => page.Find("[data-testid=shipment-totals-line-count]").TextContent.Should().Contain("2"));
        page.Find("[data-testid=shipment-totals-total-qty]").TextContent.Should().Contain("8.00");
        var amount = page.Find("[data-testid=shipment-totals-ship-amt]").TextContent;
        amount.Should().Contain("110.00", "6 x 10 + 2 x 25 without the 10% tax").And.Contain("EUR");
        page.FindAll("[data-testid=shipment-totals-base-total]").Should().BeEmpty();
        page.FindAll("[data-testid=shipment-header-tot-base-amt]").Should().BeEmpty("the shipment table lists no base total");
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void TotalsPanel_TakesTheDiscountOff_AndLeavesTheMarkedLineOut()
    {
        _api.Shipment = Shipment() with
        {
            Lines =
            [
                new ShipmentLineDto(9001, 1, 11, "BOLT", "Hex Bolt", "EA", 1, "MAIN", null, null, 4m, 10m, "D10", -10m, 1, 0m, 96m, null, null, null, null, null, null),
                new ShipmentLineDto(9002, 2, 12, "PANEL", "Panel", "EA", 1, "MAIN", null, null, 1m, 25m, null, 0m, 1, 0m, 5m, null, null, null, null, null, null),
            ],
        };
        var page = RenderAs("admin", "SH0000001");
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(2));

        page.Find("[data-testid=shipment-totals-ship-amt]").TextContent.Should().Contain("61.00", "4 x 10 less 10% + 1 x 25");

        Frame(page).Lines.MoveTo(Lines(page).KeyOf(1));
        page.Render();
        Button(page, "mark-delete").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=shipment-totals-ship-amt]").TextContent.Should().Contain("36.00"));
        page.Find("[data-testid=shipment-totals-line-count]").TextContent.Should().Contain("1");
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "SD-BR-172")]
    public void OrderImport_TakesOnlyTheCheckedLine_WithTheQtyTyped()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));

        Button(page, "import-order").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=shipment-order-import-row]").Should().HaveCount(2, "a bill-first order ships through its invoice"));
        page.Find("[data-testid=shipment-order-import-import]").HasAttribute("disabled").Should().BeTrue("no line is checked yet");

        page.Find("[data-testid=shipment-order-import-check-0]").Change(true);
        page.Find("[data-testid=shipment-order-import-qty-0]").GetAttribute("value").Should().Be("6.00", "the qty starts at the remaining qty");
        page.Find("[data-testid=shipment-order-import-qty-0]").Change("0");
        page.Find("[data-testid=shipment-order-import-import]").HasAttribute("disabled").Should().BeTrue("a checked line takes a qty above zero");
        page.Find("[data-testid=shipment-order-import-qty-0]").Change("3");
        page.Find("[data-testid=shipment-order-import-import]").Click();

        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        var line = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (line.ItemCd, line.ShipQty, line.RemainingQty).Should().Be(("BOLT", 3m, 6m));

        Button(page, "import-order").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=shipment-order-import-row]").Should().ContainSingle("the imported line is on the grid already"));
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "SD-BR-171")]
    public async Task ImportedLine_LocksTheItemAndPrice_ButNotTheWarehouseOrQty()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        ImportAllOrderLines(page);
        var lines = Lines(page);

        (await lines.EditAsync(0, "itemCd", "PANEL")).Should().BeFalse();
        (await lines.EditAsync(0, "shipPrc", "99")).Should().BeFalse();
        (await lines.EditAsync(0, "shipQty", "2")).Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UC-051-T06"), Trait("Rule", "SD-BR-175"), Trait("Rule", "SD-BR-176")]
    public async Task BillFirstLine_StaysInTheConsignmentWarehouse()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        ImportAllInvoiceLines(page);

        (await Lines(page).EditAsync(0, "warehouseCode", "MAIN")).Should().BeFalse();
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void Grid_HasTheColumnsOfTheTable_InOrder()
    {
        _api.Shipment = Shipment();

        var page = Open();

        Lines(page).Headers.Select(h => h.Field).Should().Equal(
            "seq", "soNo", "dnNo", "itemCd", "itemName", "unit", "warehouseCode", "lotNo", "shipQty", "shipPrc", "remainingQty", "onhandQty", "billedQty");
        Lines(page).IsReadOnly(0, "itemName").Should().BeTrue();
        var line = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (line.SoNo, line.RemainingQty).Should().Be(("SO2611000001", 2m));
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void Header_FollowsTheTableLayout()
    {
        _api.Shipment = Shipment();

        var page = Open();

        var fields = page.FindAll("[data-testid^=shipment-header-]").Select(e => e.GetAttribute("data-testid")!.Replace("shipment-header-", ""));
        fields.Except(["customer-lookup", "warehouse-lookup", "warehouse-name", "ship-to-toggle"]).Should().Equal(
            "customer-name", "ship-dt", "warehouse-code",
            "currency-code", "exchange-rate", "billing-flow", "sales-invoice-no",
            "ship-via",
            "ship-to", "ship-name", "ship-addr1", "ship-addr2", "ship-city", "ship-state", "ship-zip-code", "ship-country", "ship-phone",
            "remarks");
    }

    [Fact, Trait("Id", "SD-UC-051-T08"), Trait("Rule", "SD-BR-167")]
    public void InvoiceNumber_LinksToTheSalesInvoiceScreen()
    {
        _api.Shipment = Shipment();

        var page = Open();

        page.Find("[data-testid=shipment-header-sales-invoice-no]").GetAttribute("href").Should().Be("/sd/sales-invoice/IV2611000001");
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void ShipTo_IsCollapsed_UnlessTheRequiredZipIsEmpty()
    {
        _api.Shipment = Shipment();
        var page = Open();

        ShipToBody(page).HasAttribute("hidden").Should().BeTrue();
        page.Find("[data-testid=shipment-header-ship-to-toggle]").Click();
        ShipToBody(page).HasAttribute("hidden").Should().BeFalse();

        _api.Shipment = Shipment() with { ShipZipCode = null };
        var noZip = Open();
        ShipToBody(noZip).HasAttribute("hidden").Should().BeFalse();
        Button(noZip, "save").Click();
        noZip.WaitForAssertion(() => noZip.Find("[data-testid=shipment-message]").GetAttribute("data-level").Should().Be("error"));
        _api.Saved.Should().BeEmpty("the ZIP code is required");
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "BC-BR-435")]
    public void KoreanCompany_HidesTheShipToState()
    {
        _api.Policies = [new("Sd.Shipment.ShipState", FieldPolicyStates.Hidden, null, "Sd.Shipment.ShipState", "CountryPack")];

        var page = RenderAs("admin");

        page.WaitForAssertion(() => page.FindAll("[data-testid=shipment-header-ship-state]").Should().BeEmpty());
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "MM-BR-703")]
    public void HeaderWarehouse_IsPickedFromTheGeneralWarehouses()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=shipment-header-warehouse-lookup]").Click();

        page.WaitForAssertion(() => page.LookupRows("shipment-warehouse-lookup").Should().ContainSingle("the consignment warehouse is only for bill-first lines"));
        page.PickLookupRow<WarehouseOptionDto, Shipment>("shipment-warehouse-lookup", 0);
        page.WaitForAssertion(() => page.Field("shipment-header-warehouse-code").Value.Should().Be("MAIN"));
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "MM-BR-703")]
    public void HeaderWarehouse_TypedConsignmentCode_IsRefused()
    {
        var page = RenderAs("admin");

        page.Field("shipment-header-warehouse-code").Change("consign");

        page.WaitForAssertion(() => page.Find("[data-testid=shipment-message]").GetAttribute("data-level").Should().Be("error"));
        Frame(page).Header.WarehouseCode.Should().BeNull();
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void TypedUnknownCustomer_KeepsTheNotFoundMessage()
    {
        var page = RenderAs("admin");

        page.Field("shipment-header-customer-name").Change("nobody");

        page.WaitForAssertion(() => page.Find("[data-testid=shipment-message]").GetAttribute("data-level").Should().Be("error"));
        Frame(page).Header.CustomerId.Should().BeNull();
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void FindDialog_SendsTheConditions_ToTheServer_AndShowsTheAmount()
    {
        _api.Shipment = Shipment();
        var page = RenderAs("admin");
        Button(page, "search").Click();
        page.WaitForAssertion(() => page.LookupRows("shipment-find").Should().ContainSingle());

        page.Find("[data-testid=shipment-find-keyword]").Change("sh00");
        page.Find("[data-testid=shipment-find-customer]").Change("acm");
        page.Find("[data-testid=shipment-find-from]").Change("2026-11-01");
        page.Find("[data-testid=shipment-find-to]").Change("2026-11-30");
        page.Find("[data-testid=shipment-find-status]").Change("BL");
        page.Find("form.quro-search").Submit();

        page.WaitForAssertion(() => _api.Requests.Last(r => r.RequestUri!.AbsolutePath == "/api/sd/shipments").RequestUri!.Query
            .Should().Contain("shipNo=sh00").And.Contain("customerName=acm").And.Contain("status=BL").And.Contain("2026-11-01").And.Contain("2026-11-30"));
        page.WaitForAssertion(() => page.LookupRows("shipment-find").Should().ContainSingle()
            .Which.TextContent.Should().Contain("40.00 USD").And.NotContain("IV2611000001"));
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void VoidAndPrint_AreInTheOutputGroup()
    {
        _api.Shipment = Shipment();

        var page = Open();

        Button(page, "void").ParentElement!.GetAttribute("data-group").Should().Be("output");
        Button(page, "pdf").ParentElement!.GetAttribute("data-group").Should().Be("output");
        Button(page, "void").ClassList.Should().Contain("quro-danger-text");
    }

    [Fact, Trait("Id", "SD-UC-051-T04"), Trait("Rule", "SD-BR-168")]
    public void ShipThenBill_ABilledLineBlocksVoid_ButNotSave()
    {
        _api.Shipment = Shipment(status: "PB", billingFlow: "SHIP_THEN_BILL", billed: 1);

        var page = Open();

        Button(page, "void").HasAttribute("disabled").Should().BeTrue();
        Button(page, "save").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact, Trait("Id", "SD-UC-051-T02"), Trait("Rule", "SD-BR-104"), Trait("Rule", "SD-BR-167")]
    public void ShipAndBill_APaidInvoiceBlocksSaveDeleteAndVoid()
    {
        _api.Shipment = Shipment(status: "BL", billed: 4) with { InvoiceHasPayments = true };

        var page = Open();
        Frame(page).Lines.MoveTo(Lines(page).KeyOf(0));
        page.Render();

        Enabled(page).Except(["search", "new"]).Should().BeEquivalentTo(["pdf"]);
    }

    [Fact, Trait("Id", "SD-UC-051-T06"), Trait("Rule", "SD-BR-177")]
    public void BillFirst_APaidInvoiceBlocksNothing()
    {
        _api.Shipment = Shipment(status: "BL", billingFlow: "BILL_THEN_SHIP", billed: 4) with { InvoiceHasPayments = true };

        var page = Open();
        Frame(page).Lines.MoveTo(Lines(page).KeyOf(0));
        page.Render();

        Button(page, "void").HasAttribute("disabled").Should().BeFalse();
        Button(page, "mark-delete").HasAttribute("disabled").Should().BeFalse();
        Button(page, "save").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact, Trait("Id", "SD-UC-051-T06"), Trait("Rule", "SD-BR-175"), Trait("Rule", "SD-BR-176")]
    public void ImportInvoice_AddsTheUnshippedLinesFromTheConsignmentWarehouse()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));

        ImportAllInvoiceLines(page);

        var line = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (line.ItemCd, line.Unit, line.ShipQty, line.WarehouseCode, line.WarehouseId, line.SalesInvoiceLineId, line.DnNo, line.RemainingQty)
            .Should().Be(("PANEL", "EA", 4m, "CONSIGN", 2L, 7001L, "IV2611000003", 4m));
        Frame(page).Header.BillingFlow.Should().Be("BILL_THEN_SHIP");
        line.ToRequest().ShipPrc.Should().BeNull("an invoice line ships at the invoice price");
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "SD-BR-173")]
    public async Task DirectLine_TakesTheLineDefaults_AndSendsItsPriceAndTaxCode()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        var lines = Lines(page);

        Button(page, "add-row").Click();
        (await lines.EditAsync(0, "itemCd", "panel")).Should().BeTrue();

        var key = lines.KeyOf(0);
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(key).ShipPrc.Should().Be(22m));
        var line = Frame(page).Lines.GetItem(key);
        (line.ItemId, line.ShipQty, line.TaxCodeId, line.WarehouseId).Should().Be((12L, 1m, (long?)1L, (long?)1L));
        var request = line.ToRequest();
        (request.ShipPrc, request.TaxCodeId, request.SalesOrderLineId).Should().Be(((decimal?)22m, (long?)1L, (long?)null));
    }

    [Fact, Trait("Id", "SD-UT-171"), Trait("Rule", "SD-BR-171")]
    public async Task SavedLine_IsNotChanged_ButCanBeDeleted_AndTheSaveLeavesItOut()
    {
        _api.Shipment = Shipment();
        var page = Open();
        var lines = Lines(page);

        (await lines.EditAsync(0, "shipQty", "1")).Should().BeFalse("a saved line is deleted and added again, not changed");
        Frame(page).Lines.MoveTo(lines.KeyOf(0));
        page.Render();
        Button(page, "mark-delete").HasAttribute("disabled").Should().BeFalse();

        Button(page, "mark-delete").Click();
        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        _api.Saved[0].Lines.Should().BeEmpty();
        _api.Saved[0].UpdatedAt.Should().Be(AuditTimes.Updated);
    }

    [Fact, Trait("Id", "SD-UC-051-T04"), Trait("Rule", "SD-BR-168")]
    public void BilledLineOfAShipThenBillShipment_CannotBeMarkedForDelete()
    {
        _api.Shipment = Shipment(status: "BL", billingFlow: "SHIP_THEN_BILL", billed: 4);
        var page = Open();

        Frame(page).Lines.MoveTo(Lines(page).KeyOf(0));
        page.Render();

        Button(page, "mark-delete").HasAttribute("disabled").Should().BeTrue();
    }

    [Theory, Trait("Id", "SD-UT-166"), Trait("Rule", "SD-BR-166"), Trait("Rule", "SD-BR-169")]
    [InlineData("OP", "neutral", "save,add-row,mark-delete,import-order,import-invoice,void,pdf")]
    [InlineData("PB", "warn", "save,add-row,mark-delete,import-order,import-invoice,void,pdf")]
    [InlineData("BL", "ok", "save,add-row,mark-delete,import-order,import-invoice,void,pdf")]
    [InlineData("VO", "danger", "pdf")]
    public void Status_SetsTheChipAndTheEnabledButtons(string status, string chip, string enabled)
    {
        _api.Shipment = Shipment(status);

        var page = Open();
        Frame(page).Lines.MoveTo(Lines(page).KeyOf(0));
        page.Render();

        page.Find("[data-testid=shipment-status-chip] .quro-chip").ClassList.Should().Contain($"quro-chip-{chip}");
        Enabled(page).Except(["search", "new"]).Should().BeEquivalentTo(enabled.Split(','));
    }

    [Fact, Trait("Id", "SD-UC-051-T02"), Trait("Rule", "SD-BR-169")]
    public void Void_AsksFirst_ThenShowsTheVoidShipment()
    {
        _api.Shipment = Shipment();
        var page = Open();
        _confirm.Answer = true;

        Button(page, "void").Click();

        page.WaitForAssertion(() => Frame(page).Header.Status.Should().Be("VO"));
        _confirm.Questions.Should().ContainSingle().Which.Should().Contain("출하를 취소");
        _api.Voided.Should().Equal(AuditTimes.Updated);
    }

    [Fact, Trait("Id", "SD-UC-051-T09"), Trait("Rule", "MM-BR-362")]
    public async Task LotLookup_ReadsTheLotsOfTheCurrentLine_OnTheShipDate()
    {
        _api.Shipment = Shipment();
        var page = Open();
        var frame = Frame(page);
        frame.Lines.MoveTo(Lines(page).KeyOf(0));

        var rows = await page.InvokeAsync(() => frame.Lookup!(new LookupQuery("sdShipLot", null)));

        rows.Select(r => r["lotNo"]).Should().Equal("L2611A", "L2611B");
        _api.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery == "/api/mm/lots?itemId=11&warehouseId=1&date=2026-11-05");
    }

    [Fact, Trait("Id", "SD-UT-309"), Trait("Rule", "SD-BR-309")]
    public void CreditLimitWarning_Yes_SavesAgainWithTheAnswer()
    {
        _api.Shipment = Shipment();
        var page = Open();
        page.Field("shipment-header-remarks").Change("Big shipment");
        _api.SaveProblems.Enqueue("""{"status":422,"code":"SD-W-309","args":["1000.00","1200.00"],"confirmField":"confirmCreditLimit"}""");
        _confirm.Answer = true;

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().HaveCount(2));
        (_api.Saved[0].ConfirmCreditLimit, _api.Saved[1].ConfirmCreditLimit).Should().Be(((bool?)null, (bool?)true));
    }

    [Fact, Trait("Id", "SD-UT-309"), Trait("Rule", "SD-BR-309")]
    public void CreditLimitWarning_No_DoesNotSave()
    {
        _api.Shipment = Shipment();
        var page = Open();
        page.Field("shipment-header-remarks").Change("Big shipment");
        _api.SaveProblems.Enqueue("""{"status":422,"code":"SD-W-309","args":["1000.00","1200.00"],"confirmField":"confirmCreditLimit"}""");
        _confirm.Answer = false;

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=shipment-message]").GetAttribute("data-level").Should().Be("error"));
        _api.Saved.Should().ContainSingle("no answer means no second save");
    }

    [Theory, Trait("Id", "BC-UT-423"), Trait("Rule", "BC-BR-423")]
    [InlineData("shipPrc", "12.345", "단가")]
    [InlineData("shipQty", "1.234", "출하 수량")]
    public async Task TooManyDecimals_MarkTheCell_AndTheSaveNamesTheColumn_WithoutCallingTheApi(string field, string value, string label)
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        var lines = Lines(page);
        Button(page, "add-row").Click();
        (await lines.EditAsync(0, "itemCd", "panel")).Should().BeTrue();
        var key = lines.KeyOf(0);
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(key).ShipPrc.Should().Be(22m));

        (await lines.EditAsync(0, field, value)).Should().BeTrue();

        Frame(page).Lines.GetCellMark(key, field)!.Level.Should().Be(CellMarkLevel.Warning);

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=shipment-message]").TextContent.Should().StartWith(label));
        _api.Saved.Should().BeEmpty("the API is not called");
    }

    [Fact, Trait("Id", "BC-UT-423"), Trait("Rule", "BC-BR-423")]
    public async Task FixingTheValue_ClearsTheMark()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        var lines = Lines(page);
        Button(page, "add-row").Click();
        (await lines.EditAsync(0, "itemCd", "panel")).Should().BeTrue();
        var key = lines.KeyOf(0);
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(key).ShipPrc.Should().Be(22m));
        (await lines.EditAsync(0, "shipPrc", "12.345")).Should().BeTrue();

        (await lines.EditAsync(0, "shipPrc", "12.34")).Should().BeTrue();

        Frame(page).Lines.GetCellMark(key, "shipPrc").Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UT-423"), Trait("Rule", "BC-BR-423"), Trait("Rule", "SD-BR-172")]
    public void AnImportedLine_IsNotStoppedByItsSourcePrice()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        ImportAllOrderLines(page);
        var lines = Lines(page);
        page.WaitForAssertion(() => lines.Rows.Should().NotBeEmpty());
        Frame(page).Lines.GetItem(lines.KeyOf(0)).ShipPrc = 12.345m;

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().NotBeEmpty());
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void SaveAfterSomeoneElseSaved_Shows409Message()
    {
        _api.Shipment = Shipment();
        var page = Open();
        page.Field("shipment-header-remarks").Change("Late");
        _api.SaveProblems.Enqueue("""{"status":409,"code":"Common-E-004"}""");

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=shipment-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void Print_DownloadsTheDeliveryNote()
    {
        _api.Shipment = Shipment();
        var page = Open();

        Button(page, "pdf").Click();

        page.WaitForAssertion(() => JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("quroDownload"));
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/sd/shipments/701/print.pdf");
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Rule", "BC-BR-214")]
    public void UserWithoutFullAccess_CanOnlyFindAndPrint()
    {
        var page = RenderAs("user");

        Button(page, "save").HasAttribute("disabled").Should().BeTrue();
        Button(page, "add-row").HasAttribute("disabled").Should().BeTrue();
        Button(page, "import-order").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=shipment-header-customer-lookup]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void Labels_AreTheEnglishResources_InEnglish()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");
        _api.Shipment = Shipment();

        var page = Open();

        page.Find("[data-testid=shipment-title] h1").TextContent.Should().Be("Shipment");
        page.Find(".quro-entry-path").TextContent.Should().Be("SD > Sales");
        page.Find("[data-testid=shipment-totals-ship-amt]").TextContent.Should().Contain("Shipment amount");
        page.Find("[data-testid=shipment-totals-total-qty]").TextContent.Should().Contain("Total qty");
        Button(page, "import-order").TextContent.Should().Be("Import order");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["Item code", "Warehouse", "Lot", "Ship qty", "Billed qty", "On hand"]);
        page.Find("[data-testid=shipment-status-chip]").TextContent.Should().Contain("Open");
    }

    [Fact, Trait("Id", "SD-UC-051-T01")]
    public void Labels_AreTheKoreanResources_InKorean()
    {
        _api.Shipment = Shipment();

        var page = Open();

        page.Find(".quro-entry-path").TextContent.Should().Be("SD > 판매");
        page.Find("[data-testid=shipment-totals-ship-amt]").TextContent.Should().Contain("출하 금액");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["수주 번호", "송장 번호", "남은 수량", "현재고", "청구 수량"]);
    }

    private IRenderedComponent<Shipment> RenderAs(string role, string? shipNo = null)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Shipment>(p => p.Add(x => x.ShipNo, shipNo));
        page.WaitForElement("[data-testid=shipment-header-customer-name]");
        return page;
    }

    private IRenderedComponent<Shipment> Open()
    {
        var page = RenderAs("admin", "SH0000001");
        page.WaitForAssertion(() => page.Find("[data-testid=shipment-doc-no]").TextContent.Should().Be("SH0000001"));
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        return page;
    }

    private static void PickCustomer(IRenderedComponent<Shipment> page)
    {
        page.Find("[data-testid=shipment-header-customer-lookup]").Click();
        page.WaitForAssertion(() => page.LookupRows("shipment-customer-lookup").Should().NotBeEmpty());
        page.PickLookupRow<CustomerDto, Shipment>("shipment-customer-lookup", 0);
    }

    private static void ImportAllOrderLines(IRenderedComponent<Shipment> page) => ImportAll(page, "import-order", "shipment-order-import", 2);

    private static void ImportAllInvoiceLines(IRenderedComponent<Shipment> page) => ImportAll(page, "import-invoice", "shipment-invoice-import", 1);

    private static void ImportAll(IRenderedComponent<Shipment> page, string button, string dialog, int lines)
    {
        Button(page, button).Click();
        page.WaitForAssertion(() => page.FindAll($"[data-testid={dialog}-row]").Should().HaveCount(lines));
        page.Find($"[data-testid={dialog}-check-all]").Change(true);
        page.Find($"[data-testid={dialog}-import]").Click();
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(lines));
    }

    private static AngleSharp.Dom.IElement ShipToBody(IRenderedComponent<Shipment> page) =>
        page.Find("[data-testid=shipment-header-ship-to] .quro-field-section-body");

    private static DataGridPage<ShipmentLine> Lines(IRenderedComponent<Shipment> page) =>
        page.DataGrid<ShipmentLine, Shipment>("shipment-line");

    private static QuroHeaderDetail<ShipmentHeader, ShipmentLine> Frame(IRenderedComponent<Shipment> page) =>
        page.FindComponent<QuroHeaderDetail<ShipmentHeader, ShipmentLine>>().Instance;

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Shipment> page, string action) =>
        page.Find($"[data-testid={Screen}-toolbar-{action}]");

    private static IReadOnlyList<string> Enabled(IRenderedComponent<Shipment> page) =>
        [.. page.FindAll("[role=toolbar] button:not([disabled])").Select(b => b.GetAttribute("data-testid")!.Replace($"{Screen}-toolbar-", ""))
            .Where(b => b != "show-all-fields")];

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private static ShipmentDto Shipment(string status = "OP", string billingFlow = "SHIP_AND_BILL", decimal billed = 0) => new(
        Id: 701, ShipNo: "SH0000001", Status: status, BillingFlow: billingFlow, CustomerId: 801, CustomerName: "ACME", SalesOrderId: 601,
        SoNo: "SO2611000001", ShipDt: new DateOnly(2026, 11, 5), WarehouseId: 1, CurrencyCode: "USD", ExchangeRate: 1m, ShipVia: "UPS",
        ShipName: "ACME DOCK", ShipAddr1: "1 Main St", ShipAddr2: null, ShipCity: "Springfield", ShipState: "CA", ShipZipCode: "90001",
        ShipCountry: "USA", ShipPhone: null, Remarks: null, SalesInvoiceId: billingFlow == "SHIP_AND_BILL" ? 901 : null,
        SalesInvoiceNo: billingFlow == "SHIP_AND_BILL" ? "IV2611000001" : null,
        Lines: [new ShipmentLineDto(9001, 1, 11, "BOLT", "Hex Bolt", "EA", 1, "MAIN", null, null, 4m, 10m, null, 0m, 1, billed, 96m, 5001,
            "SO2611000001", 1, null, null, null, 2m)],
        UpdatedAt: AuditTimes.Updated, CreatedAt: AuditTimes.Created, CreatedBy: "setup", UpdatedBy: "kim");

    /// <summary>Shipment, sales order line defaults, customer, item, rate, localization, tax code, warehouse, profile, lot and code API.</summary>
    private sealed class FakeShipmentApi : HttpMessageHandler
    {
        private static readonly CustomerDto Acme = new(
            Id: 801, CName: "ACME", ShipName: "ACME DOCK", Contact: "Kim", Contact2: null, Phone1: "555-0100", Phone2: null, Phone3: null,
            FaxNo: null, Email: null, Rep: "KIM", PriceLevel: "ACME", FreightCharge: "N", BalAmt: 70.5m, Remarks: null, Memo2: null,
            Memo3: null, Terms: "NET_30", ValidFlag: "Y", Addr1: "1 Main St", Addr2: null, City: "Springfield", State: "CA",
            Country: "USA", ZipCode: "90001", ShipAddr1: "1 Main St", ShipAddr2: null, ShipCity: "Springfield", ShipState: "CA",
            ShipCountry: "USA", ShipZipCode: "90001", ShipPhone: null, ShipFax: null, CreditLimit: 1000m, DcStatus: null,
            StartDt: new DateOnly(2026, 1, 5), BillingFlow: "SHIP_AND_BILL", CurrencyCode: null, TaxCodeId: 1,
            TaxCode: "ST10", TaxPartyType: null, BusinessRegNo: null, CeoName: null, BusinessType: null, BusinessItem: null,
            CreatedAt: AuditTimes.Created, CreatedBy: "setup", UpdatedAt: AuditTimes.Updated, UpdatedBy: "kim");

        private static readonly ItemDto[] Items =
        [
            Item(11, "BOLT", "Hex Bolt", "EA", 12m),
            Item(12, "PANEL", "Panel", "EA", 25m),
        ];

        public List<HttpRequestMessage> Requests { get; } = [];

        public IReadOnlyList<FieldPolicyDto> Policies { get; set; } = [];

        public List<SaveShipmentRequest> Saved { get; } = [];

        public List<DateTimeOffset> Voided { get; } = [];

        public ShipmentDto? Shipment { get; set; }

        /// <summary>The currency and rate of the open orders.</summary>
        public (string Code, decimal Rate) OrderCurrency { get; set; } = ("USD", 1m);

        /// <summary>The bodies of the next saves' errors, in order; a save without one passes.</summary>
        public Queue<string> SaveProblems { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            if (path.StartsWith("/api/codes/", StringComparison.Ordinal))
            {
                return Json(Codes(path["/api/codes/".Length..]));
            }
            if (path.StartsWith("/" + FieldPolicyRoutes.Base, StringComparison.Ordinal))
            {
                return Json(Policies);
            }
            switch (path)
            {
                case "/" + LocalizationRoutes.Base:
                    return Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "LINE", [],
                        [new CurrencyDto("USD", "US Dollar", 2, "US"), new CurrencyDto("EUR", "Euro", 2, "DE")],
                        [new CountryDecimalDto("US", "SD", 2)],
                        [new DecimalSettingDto("SD", 2, "HALF_UP")]));
                case "/" + TaxCodeRoutes.Base:
                    return Json(new List<TaxCodeDto>
                    {
                        new(1, "ST10", "Sales tax 10%", new Dictionary<string, string>(), "SALES_TAX", 10m, null, null, false, true, true, AuditTimes.Updated),
                        new(2, "NONE", "Exempt", new Dictionary<string, string>(), "NONE", 0m, null, null, false, true, false, AuditTimes.Updated),
                    });
                case "/" + WarehouseRoutes.Options:
                    return Json(new List<WarehouseOptionDto> { new(1, "MAIN", "Main Store", "NORMAL", true), new(2, "CONSIGN", "Consignment", "CONSIGNMENT", true) });
                case "/" + WarehouseRoutes.Base:
                    return Json(new List<WarehouseDto> { new(1, "MAIN", "Main Store", true, true, "NORMAL", null, AuditTimes.Updated) });
                case "/" + UserProfileRoutes.Base:
                    return Json(new UserProfileDto("kim", "Kim", null, null, "ko", 1, "MAIN", "Main Store", AuditTimes.Updated));
                case "/" + LotRoutes.Base:
                    return Json(new List<LotBalanceDto> { new(1, "L2611A", 30m), new(2, "L2611B", 20m) });
                case "/api/sd/customers":
                    return Json(new CustomerListDto([Acme], 1));
                case "/" + ItemRoutes.Base:
                    var code = query["itemCd"];
                    var name = query["itemName"];
                    return Json(new ItemListDto([.. Items.Where(i =>
                        (string.IsNullOrEmpty(code) || i.ItemCd.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                        && (string.IsNullOrEmpty(name) || (i.ItemName ?? "").Contains(name, StringComparison.OrdinalIgnoreCase)))], Items.Length));
                case "/api/sd/sales-orders/line-defaults":
                    var item = Items.First(i => i.ItemCd.Equals(query["itemCd"], StringComparison.OrdinalIgnoreCase));
                    return Json(new SalesOrderLineDefaultsDto(item.Id, item.ItemCd, item.ItemName, item.Unit, 1m, 22m, item.SoUsdPrc, null, 0m, 1, null));
                case "/api/sd/shipments/open-order-lines":
                    return Json(new List<OpenSalesOrderDto> { Order(601, "SO2611000001", "SHIP_AND_BILL"), Order(602, "SO2611000002", "BILL_THEN_SHIP") });
                case "/api/sd/shipments/unshipped-invoice-lines":
                    return Json(new List<UnshippedInvoiceLineDto>
                    {
                        new(7001, 901, "IV2611000003", 1, 12, "PANEL", "Panel", 5m, 1m, 0m, 4m, 25m, null, 0m, 1, "USD", 1m, "EA"),
                    });
                case "/api/sd/shipments" when request.Method == HttpMethod.Get:
                    return Json(Shipment is null
                        ? new List<ShipmentSummaryDto>()
                        : [new(Shipment.Id, Shipment.ShipNo, Shipment.Status, Shipment.BillingFlow, Shipment.ShipDt, Shipment.CustomerId,
                            Shipment.CustomerName, Shipment.CurrencyCode, Shipment.SalesInvoiceNo, 40m)]);
                case "/api/sd/shipments" when request.Method == HttpMethod.Post:
                case "/api/sd/shipments/701" when request.Method == HttpMethod.Put:
                    Saved.Add((await request.Content!.ReadFromJsonAsync<SaveShipmentRequest>(cancellationToken))!);
                    if (SaveProblems.TryDequeue(out var problem))
                    {
                        var status = problem.Contains("\"status\":409", StringComparison.Ordinal) ? HttpStatusCode.Conflict : HttpStatusCode.UnprocessableEntity;
                        return new HttpResponseMessage(status) { Content = new StringContent(problem, null, "application/problem+json") };
                    }
                    var saved = Saved[^1];
                    return Json(ShipmentScreenTests.Shipment() with
                    {
                        Lines = [.. saved.Lines.Select((l, i) => new ShipmentLineDto(
                            l.Id ?? 9100 + i, i + 1, l.ItemId, Items.First(x => x.Id == l.ItemId).ItemCd, l.ItemName, "EA", l.WarehouseId ?? 1, "MAIN",
                            null, l.LotNo, l.ShipQty, l.ShipPrc ?? 10m, null, 0m, l.TaxCodeId ?? 1, 0m, 0m, l.SalesOrderLineId, null, null,
                            l.SalesInvoiceLineId, null, null))],
                    });
                case "/api/sd/shipments/701/void":
                    Voided.Add((await request.Content!.ReadFromJsonAsync<VoidShipmentRequest>(cancellationToken))!.UpdatedAt);
                    return Json(Shipment! with { Status = "VO" });
                case "/api/sd/shipments/701":
                    return Json(Shipment);
                case "/api/sd/shipments/701/print.pdf":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF-1.7"u8.ToArray()) };
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private OpenSalesOrderDto Order(long id, string soNo, string billingFlow) => new(
            id, soNo, new DateOnly(2026, 11, 4), OrderCurrency.Code, OrderCurrency.Rate, billingFlow, "KIM", "UPS", "ACME DOCK", "1 Main St", null, "Springfield", "CA",
            "90001", "USA", null,
            [
                new(5001, 1, 11, "BOLT", "Hex Bolt", "EA", 10m, 4m, 6m, 10m, null, 0m, 1),
                new(5002, 2, 12, "PANEL", "Panel", "EA", 2m, 0m, 2m, 25m, null, 0m, 1),
            ]);

        private static IReadOnlyList<CodeItemDto> Codes(string major) => major switch
        {
            "STATE" => [new("CA", "California"), new("NY", "New York")],
            "SHIPMENT_VIA" => [new("UPS", "UPS")],
            _ => [],
        };

        private static ItemDto Item(long id, string code, string name, string unit, decimal salesPrice) => new(
            id, code, name, null, null, "INVENTORY", unit, null, "Y", 0m, salesPrice, null, null, 0m, null, null, null, null, null, null, true,
            "TAXABLE", "", 0m, 0m, 0m, AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

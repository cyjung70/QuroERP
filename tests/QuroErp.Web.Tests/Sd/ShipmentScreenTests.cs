using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "SD-UC-051-T01"), Trait("Rule", "MM-BR-703")]
    public void New_StartsFromTheUsersWarehouse_WithImportAndVoidDisabled_UntilACustomerIsPicked()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=shipment-doc-no]").TextContent.Should().Be("새 문서");
        page.Field("shipment-header-warehouse-code").Selected.Should().BeEquivalentTo("MAIN");
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

        Button(page, "import-order").Click();
        page.WaitForAssertion(() => page.LookupRows("shipment-order-lookup").Should().HaveCount(1, "a bill-first order ships through its invoice"));
        page.PickLookupRow<OpenSalesOrderDto, Shipment>("shipment-order-lookup", 0);

        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(2));
        var first = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (first.ItemCd, first.ShipQty, first.ShipPrc, first.WarehouseCode, first.WarehouseId, first.SalesOrderLineId, first.Source, first.ShipAmt)
            .Should().Be(("BOLT", 6m, 10m, "MAIN", 1L, 5001L, "SO2611000001-1", 60m));
        (Frame(page).Header.BillingFlow, Frame(page).Header.ShipVia, Frame(page).Header.ShipName).Should().Be(("SHIP_AND_BILL", "UPS", "ACME DOCK"));
        page.Find("[data-testid=shipment-totals-total]").TextContent.Should().Contain("121.00");

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

    [Fact, Trait("Id", "SD-UC-051-T06"), Trait("Rule", "SD-BR-175"), Trait("Rule", "SD-BR-176")]
    public void ImportInvoice_AddsTheUnshippedLinesFromTheConsignmentWarehouse()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));

        Button(page, "import-invoice").Click();
        page.WaitForAssertion(() => page.LookupRows("shipment-invoice-lookup").Should().HaveCount(1));
        page.PickLookupRow<ShipmentInvoiceChoice, Shipment>("shipment-invoice-lookup", 0);

        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        var line = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (line.ItemCd, line.ShipQty, line.WarehouseCode, line.WarehouseId, line.SalesInvoiceLineId, line.Source)
            .Should().Be(("PANEL", 4m, "CONSIGN", 2L, 7001L, "IV2611000003-1"));
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
        (line.ItemId, line.ShipQty, line.TaxCode, line.WarehouseId, line.ShipAmt).Should().Be((12L, 1m, "ST10", 1L, 22m));
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
    [InlineData("OP", "brand", "save,add-row,mark-delete,import-order,import-invoice,void,pdf")]
    [InlineData("BL", "ok", "save,add-row,mark-delete,import-order,import-invoice,void,pdf")]
    [InlineData("VO", "neutral", "pdf")]
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
        Button(page, "import-order").TextContent.Should().Be("Import order");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["Item code", "Warehouse", "Lot", "Ship qty", "Billed qty", "On hand"]);
        page.Find("[data-testid=shipment-status-chip]").TextContent.Should().Contain("Open");
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

    private static DataGridPage<ShipmentLine> Lines(IRenderedComponent<Shipment> page) =>
        page.DataGrid<ShipmentLine, Shipment>("shipment-line");

    private static QuroHeaderDetail<ShipmentHeader, ShipmentLine> Frame(IRenderedComponent<Shipment> page) =>
        page.FindComponent<QuroHeaderDetail<ShipmentHeader, ShipmentLine>>().Instance;

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Shipment> page, string action) =>
        page.Find($"[data-testid={Screen}-toolbar-{action}]");

    private static IReadOnlyList<string> Enabled(IRenderedComponent<Shipment> page) =>
        [.. page.FindAll("[role=toolbar] button:not([disabled])").Select(b => b.GetAttribute("data-testid")!.Replace($"{Screen}-toolbar-", ""))];

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private static ShipmentDto Shipment(string status = "OP", string billingFlow = "SHIP_AND_BILL", decimal billed = 0) => new(
        Id: 701, ShipNo: "SH0000001", Status: status, BillingFlow: billingFlow, CustomerId: 801, CustomerName: "ACME", SalesOrderId: 601,
        SoNo: "SO2611000001", ShipDt: new DateOnly(2026, 11, 5), WarehouseId: 1, CurrencyCode: "USD", ExchangeRate: 1m, ShipVia: "UPS",
        ShipName: "ACME DOCK", ShipAddr1: "1 Main St", ShipAddr2: null, ShipCity: "Springfield", ShipState: "CA", ShipZipCode: "90001",
        ShipCountry: "USA", ShipPhone: null, Remarks: null, SalesInvoiceId: billingFlow == "SHIP_AND_BILL" ? 901 : null,
        SalesInvoiceNo: billingFlow == "SHIP_AND_BILL" ? "IV2611000001" : null,
        Lines: [new ShipmentLineDto(9001, 1, 11, "BOLT", "Hex Bolt", "EA", 1, "MAIN", null, null, 4m, 10m, null, 0m, 1, billed, 96m, 5001,
            "SO2611000001", 1, null, null, null)],
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

        public List<SaveShipmentRequest> Saved { get; } = [];

        public List<DateTimeOffset> Voided { get; } = [];

        public ShipmentDto? Shipment { get; set; }

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
                        new(7001, 901, "IV2611000003", 1, 12, "PANEL", "Panel", 5m, 1m, 0m, 4m, 25m, null, 0m, 1, "USD", 1m),
                    });
                case "/api/sd/shipments" when request.Method == HttpMethod.Get:
                    return Json(Shipment is null
                        ? new List<ShipmentSummaryDto>()
                        : [new(Shipment.Id, Shipment.ShipNo, Shipment.Status, Shipment.BillingFlow, Shipment.ShipDt, Shipment.CustomerId,
                            Shipment.CustomerName, Shipment.CurrencyCode, Shipment.SalesInvoiceNo)]);
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

        private static OpenSalesOrderDto Order(long id, string soNo, string billingFlow) => new(
            id, soNo, new DateOnly(2026, 11, 4), "USD", 1m, billingFlow, "KIM", "UPS", "ACME DOCK", "1 Main St", null, "Springfield", "CA",
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

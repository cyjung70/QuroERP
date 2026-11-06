using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quro.Grid.Data;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Bc.PurchasePrices;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Contracts.Mm.Vendors;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Codes;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.PurchasePrices;
using QuroErp.Web.Bc.Tax;
using QuroErp.Web.Bc.UserProfiles;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.PurchaseOrders;
using QuroErp.Web.Mm.Vendors;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Purchase order screen /mm/purchase-order (MM-UC-003, TASK-MM-006) against a fake API. The company is KR (base KRW).</summary>
public class PurchaseOrderScreenTests : BunitContext
{
    private const string Screen = "purchase-order";
    private readonly FakePurchaseOrderApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public PurchaseOrderScreenTests()
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
        Services.AddHttpClient<PurchaseOrderApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<VendorApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<PurchasePriceApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ExchangeRateApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<TaxCodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<UserProfileApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UC-003-T01")]
    public void New_StartsInTheBaseCurrency_WithTheUsersWarehouse_AndOnlySaveEnabled()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=purchase-order-doc-no]").TextContent.Should().Be("새 문서");
        page.FindAll("[data-testid=purchase-order-status-chip]").Should().BeEmpty();
        page.Field("purchase-order-header-currency-code").Selected.Should().BeEquivalentTo("KRW");
        page.Field("purchase-order-header-warehouse-code").Selected.Should().BeEquivalentTo("MAIN");
        page.Field("purchase-order-header-warehouse-code").Options.Select(o => o.Code).Should().NotContain("consign", "consignment warehouses never receive from a PO");
        page.FindAll("[data-testid=purchase-order-header-tot-base-amt]").Should().BeEmpty("the base total is shown for another currency only");
        Enabled(page).Should().BeEquivalentTo("search", "new", "save", "add-row");
        page.Find(".quro-entry-lines-head").TextContent.Should().Contain("거래처를 먼저 선택하세요.");
    }

    [Fact, Trait("Id", "MM-UT-031"), Trait("Rule", "MM-BR-031"), Trait("Rule", "BC-BR-542")]
    public void PickingAVendor_FillsItsCurrencyRateAndDefaultTaxCode_AndShowsTheBaseTotal()
    {
        var page = RenderAs("admin");

        PickVendor(page);

        page.WaitForAssertion(() => page.Field("purchase-order-header-exchange-rate").Value.Should().Be("1382.5"));
        page.Field("purchase-order-header-vendor-name").Value.Should().Be("ACME");
        page.Field("purchase-order-header-currency-code").Selected.Should().BeEquivalentTo("USD");
        page.Field("purchase-order-header-tax-code").Selected.Should().BeEquivalentTo("VAT10");
        page.FindAll("[data-testid=purchase-order-header-tot-base-amt]").Should().ContainSingle();
        _api.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery.StartsWith("/api/bc/exchange-rates?", StringComparison.Ordinal)
                                            && r.RequestUri.Query.Contains("currencyCode=USD", StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "MM-UT-016"), Trait("Rule", "MM-BR-016")]
    public async Task NewOrder_ItemTakesTheGroupPrice_ThenTheStandardPrice_AndSavePostsTheLines()
    {
        var page = RenderAs("admin");
        PickVendor(page);
        page.WaitForAssertion(() => page.Field("purchase-order-header-exchange-rate").Value.Should().Be("1382.5"));
        var lines = Lines(page);

        Button(page, "add-row").Click();
        (await lines.EditAsync(0, "itemCd", "bolt")).Should().BeTrue();
        await lines.EditAsync(0, "poQty", "2");
        Button(page, "add-row").Click();
        (await lines.EditAsync(1, "itemCd", "FREIGHT")).Should().BeTrue();
        await lines.EditAsync(1, "poQty", "1");

        page.WaitForAssertion(() => Frame(page).Lines.GetItem(lines.KeyOf(1)).PoPrc.Should().Be(75m));
        var bolt = Frame(page).Lines.GetItem(lines.KeyOf(0));
        (bolt.ItemCd, bolt.ItemId, bolt.PoPrc, bolt.PoAmt, bolt.TaxAmt, bolt.Unit).Should().Be(("BOLT", 11L, 9.5m, 19.00m, 1.90m, "EA"));
        bolt.TaxCode.Should().Be("VAT10", "a new line takes the header's default tax code");
        page.Find("[data-testid=purchase-order-totals-total]").TextContent.Should().Contain("103.40").And.Contain("USD");

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        var request = _api.Saved[0];
        (request.VendorId, request.CurrencyCode, request.ExchangeRate, request.WarehouseId, request.UpdatedAt).Should().Be((701L, "USD", 1382.5m, 1L, (DateTimeOffset?)null));
        request.Lines.Select(l => (l.ItemId, l.PoQty, l.PoPrc, l.TaxCodeId, l.WarehouseId)).Should().Equal((11L, 2m, 9.5m, 1L, 1L), (12L, 1m, 75m, 1L, 1L));
        page.WaitForAssertion(() => page.Find("[data-testid=purchase-order-doc-no]").TextContent.Should().Be("PO2611000001"));
    }

    [Fact, Trait("Id", "MM-UC-003-T09"), Trait("Rule", "MM-BR-026")]
    public void AddRow_CopiesTheHeaderDueDateWarehouseAndTaxCode()
    {
        var page = Open();
        var lines = Lines(page);

        Button(page, "add-row").Click();

        var added = Frame(page).Lines.GetItem(lines.KeyOf(1));
        (added.DueDt, added.WarehouseCode, added.WarehouseId, added.TaxCode, added.TaxCodeId)
            .Should().Be((new DateOnly(2026, 11, 30), "MAIN", 1L, "VAT10", 1L));
    }

    [Theory, Trait("Id", "MM-UC-003-T11"), Trait("Rule", "MM-BR-019")]
    [InlineData("DR", 0, "neutral", "save,add-row,mark-delete,approve,delete,pdf,export")]
    [InlineData("PO", 0, "brand", "save,add-row,mark-delete,close,cancel,delete,pdf,export")]
    [InlineData("RE", 4, "warn", "close,pdf,export")]
    [InlineData("CL", 10, "ok", "pdf,export")]
    [InlineData("MC", 4, "neutral", "pdf,export")]
    [InlineData("CN", 0, "danger", "pdf,export")]
    public void Status_SetsTheChipAndTheEnabledButtons(string status, int received, string chip, string enabled)
    {
        _api.Order = Order(status, received);

        var page = Open();

        page.Find("[data-testid=purchase-order-status-chip] .quro-chip").ClassList.Should().Contain($"quro-chip-{chip}");
        Enabled(page).Except(["search", "new"]).Should().BeEquivalentTo(enabled.Split(','));
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid")).Should().Contain(
            ["purchase-order-toolbar-approve", "purchase-order-toolbar-close", "purchase-order-toolbar-cancel"], "disabled buttons stay");
    }

    [Fact, Trait("Id", "MM-UT-013"), Trait("Rule", "MM-BR-013")]
    public void ReceivedOrder_LocksEveryLineCell()
    {
        _api.Order = Order("RE", 4);

        var page = Open();

        Lines(page).IsReadOnly(0, "poQty").Should().BeTrue();
        Lines(page).IsReadOnly(0, "itemCd").Should().BeTrue();
        page.Field("purchase-order-header-remarks").IsReadOnly.Should().BeTrue();
    }

    [Fact, Trait("Id", "MM-UT-021"), Trait("Rule", "MM-BR-021")]
    public void ChangedApprovedOrder_ShowsTheRevision_AndTheApprovedAudit()
    {
        _api.Order = Order("PO", 0, revision: 2);

        var page = Open();

        page.Find("[data-testid=purchase-order-revision]").TextContent.Should().Be("Rev. 2");
        page.Find("[data-testid=purchase-order-audit]").TextContent.Should().Contain("승인").And.Contain("boss");
    }

    [Fact, Trait("Id", "MM-UC-003-T02"), Trait("Rule", "MM-BR-020")]
    public void Approve_AsksFirst_ThenShowsTheApprovedOrder()
    {
        var page = Open();

        Button(page, "approve").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=purchase-order-status-chip]").TextContent.Should().Be("승인"));
        _confirm.Questions.Should().Equal("이 발주를 승인할까요?");
        _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/mm/purchase-orders/501/approve");
    }

    [Fact, Trait("Id", "MM-UT-020"), Trait("Rule", "MM-BR-020")]
    public void Approve_WithUnsavedChanges_AsksToSaveFirst()
    {
        var page = Open();
        page.Field("purchase-order-header-remarks").Change("Call first");

        Button(page, "approve").Click();

        page.Find("[data-testid=purchase-order-message]").GetAttribute("data-level").Should().Be("warn");
        _confirm.Questions.Should().BeEmpty();
        _api.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath.EndsWith("/approve", StringComparison.Ordinal));
    }

    [Theory, Trait("Id", "MM-UT-016"), Trait("Rule", "MM-BR-016")]
    [InlineData(true)]
    [InlineData(false)]
    public void VendorPriceWarning_AsksAndSendsTheAnswer_AndEitherAnswerSaves(bool answer)
    {
        var page = Open();
        page.Field("purchase-order-header-remarks").Change("New price");
        _api.SaveProblem = """{"status":422,"code":"MM-W-016","args":["BOLT"],"confirmField":"updateVendorPrice"}""";
        _confirm.Answer = answer;

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().HaveCount(2));
        _api.Saved.Select(s => s.UpdateVendorPrice).Should().Equal(null, answer);
        _confirm.Questions.Should().ContainSingle().Which.Should().Contain("BOLT");
        page.WaitForAssertion(() => page.Find("[data-testid=purchase-order-message]").TextContent.Should().Be("저장했습니다."));
    }

    [Fact, Trait("Id", "MM-UC-003-T03")]
    public void SaveAfterSomeoneElseSaved_Shows409Message()
    {
        var page = Open();
        page.Field("purchase-order-header-remarks").Change("Late");
        _api.SaveProblem = """{"status":409,"code":"Common-E-004"}""";
        _api.SaveProblemStatus = HttpStatusCode.Conflict;

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=purchase-order-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "MM-UT-015"), Trait("Rule", "MM-BR-015")]
    public async Task RestoreRow_RevertsOnlyThatLine_AndKeepsTheHeaderChange()
    {
        var page = Open();
        var lines = Lines(page);
        page.Field("purchase-order-header-remarks").Change("Changed remarks");
        await lines.EditAsync(0, "poQty", "12");
        var key = lines.KeyOf(0);
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(key).PoAmt.Should().Be(15000m));

        lines.Select(0);
        Button(page, "restore-row").Click();

        var line = Frame(page).Lines.GetItem(key);
        (line.PoQty, line.PoAmt, line.TaxAmt).Should().Be((10m, 12500m, 1250m));
        Frame(page).Lines.GetState(key).Should().Be(RowState.Normal);
        Frame(page).IsDirty.Should().BeTrue("the header change is not saved");
        Button(page, "save").HasAttribute("disabled").Should().BeFalse();
        page.Field("purchase-order-header-remarks").Value.Should().Be("Changed remarks");
    }

    [Fact, Trait("Id", "MM-UC-003-T07")]
    public void Pdf_DownloadsThePurchaseOrderForm()
    {
        var page = Open();

        Button(page, "pdf").Click();

        page.WaitForAssertion(() => JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("quroDownload"));
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/mm/purchase-orders/501/print/purchase-order.pdf");
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Rule", "BC-BR-214")]
    public void UserWithoutFullAccess_CanOnlyFindAndPrint()
    {
        var page = RenderAs("user");

        Button(page, "save").HasAttribute("disabled").Should().BeTrue("a read-only user cannot save a new order either");
        Button(page, "add-row").HasAttribute("disabled").Should().BeTrue();
        page.Field("purchase-order-header-remarks").IsReadOnly.Should().BeTrue();
    }

    [Fact, Trait("Id", "MM-UC-003-T11")]
    public void Labels_AreTheEnglishResources_InEnglish()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = Open();

        page.Find("[data-testid=purchase-order-title] h1").TextContent.Should().Be("PO Entry");
        Button(page, "approve").TextContent.Should().Be("Approve");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["Item Code", "Unit Price", "Disc. %", "Received"]);
        page.Find("[data-testid=purchase-order-totals-total]").TextContent.Should().Contain("13,750.00");
    }

    [Fact]
    public void Pdf_SharesThePrintIcon_AndExcelHasItsOwn()
    {
        var page = Open();

        page.Find("[data-testid=purchase-order-toolbar-pdf] .quro-command-icon").GetAttribute("data-icon").Should().Be("print");
        page.Find("[data-testid=purchase-order-toolbar-export] .quro-command-icon").GetAttribute("data-icon").Should().Be("excel");
    }

    private IRenderedComponent<PurchaseOrder> RenderAs(string role, string? poNo = null)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<PurchaseOrder>(p => p.Add(x => x.PoNo, poNo));
        page.WaitForElement("[data-testid=purchase-order-header-vendor-name]");
        return page;
    }

    private IRenderedComponent<PurchaseOrder> Open()
    {
        var page = RenderAs("admin", "PO2611000001");
        page.WaitForAssertion(() => page.Find("[data-testid=purchase-order-doc-no]").TextContent.Should().Be("PO2611000001"));
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        return page;
    }

    private static void PickVendor(IRenderedComponent<PurchaseOrder> page)
    {
        page.Find("[data-testid=purchase-order-header-vendor-lookup]").Click();
        page.WaitForAssertion(() => page.LookupRows("purchase-order-vendor-lookup").Should().NotBeEmpty());
        page.PickLookupRow<VendorDto, PurchaseOrder>("purchase-order-vendor-lookup", 0);
    }

    private static DataGridPage<PurchaseOrderLine> Lines(IRenderedComponent<PurchaseOrder> page) =>
        page.DataGrid<PurchaseOrderLine, PurchaseOrder>("purchase-order-line");

    private static QuroHeaderDetail<PurchaseOrderHeader, PurchaseOrderLine> Frame(IRenderedComponent<PurchaseOrder> page) =>
        page.FindComponent<QuroHeaderDetail<PurchaseOrderHeader, PurchaseOrderLine>>().Instance;

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<PurchaseOrder> page, string action) =>
        page.Find($"[data-testid={Screen}-toolbar-{action}]");

    private static IReadOnlyList<string> Enabled(IRenderedComponent<PurchaseOrder> page) =>
        [.. page.FindAll("[role=toolbar] button:not([disabled])").Select(b => b.GetAttribute("data-testid")!.Replace($"{Screen}-toolbar-", ""))];

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private static PurchaseOrderDto Order(string status = "DR", decimal received = 0, int revision = 0) => new(
        501, "PO2611000001", status, 701, "ACME", new DateOnly(2026, 11, 4), "USD", 1382.5m, "FOB", new DateOnly(2026, 11, 30), 1,
        "NET 30", "KIM", "Rush", 12500m, 0m, 12500m, 1250m, 13750m, 19009375m, revision,
        status == "DR" ? null : AuditTimes.Updated, status == "DR" ? null : "boss", null, null, null, null, received,
        [new PurchaseOrderLineDto(9001, 1, 11, "BOLT", "Hex Bolt", 10m, 1250m, 12500m, 0m, 0m, 12500m, 1, 12500m, 1250m,
            new DateOnly(2026, 11, 30), 1, received, "EA")],
        AuditTimes.Updated, AuditTimes.Created, "setup", "kim");

    /// <summary>Purchase order, vendor, item, price, rate, localization, tax code, warehouse, code and profile API.</summary>
    private sealed class FakePurchaseOrderApi : HttpMessageHandler
    {
        private static readonly VendorDto Acme = new(701, "ACME", "Kim", null, "555-0100", null, null, null, null, "NET_30", "Y", 0m,
            null, null, null, null, null, "USA", null, null, null, null, null, null, null,
            "ACME", "USD", 1, "VAT10", null, null, null, null, null, AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

        private static readonly ItemDto[] Items =
        [
            Item(11, "BOLT", "Hex Bolt", "INVENTORY", "EA", 0.45m),
            Item(12, "FREIGHT", "Freight", "SERVICE", "LOT", 75m),
            Item(13, "KIT", "Kit", "SET", "EA", 10m),
        ];

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<SavePurchaseOrderRequest> Saved { get; } = [];

        public PurchaseOrderDto Order { get; set; } = PurchaseOrderScreenTests.Order();

        /// <summary>The body of the next save's error; the save after it passes.</summary>
        public string? SaveProblem { get; set; }

        public HttpStatusCode SaveProblemStatus { get; set; } = HttpStatusCode.UnprocessableEntity;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            switch (path)
            {
                case "/" + LocalizationRoutes.Base:
                    return Json(new LocalizationDto("KR", "KRW", "ko", "Asia/Seoul", "VAT", "yyyy-MM-dd", "LINE", [],
                        [new CurrencyDto("KRW", "Won", 0, "KR"), new CurrencyDto("USD", "US Dollar", 2, "US")],
                        [new CountryDecimalDto("KR", "MM", 0), new CountryDecimalDto("US", "MM", 4)],
                        [new DecimalSettingDto("MM", 2, "HALF_UP")]));
                case "/" + TaxCodeRoutes.Base:
                    return Json(new List<TaxCodeDto>
                    {
                        new(1, "VAT10", "과세 10%", new Dictionary<string, string>(), "VAT", 10m, null, null, false, true, true, AuditTimes.Updated),
                        new(2, "NONE", "비과세", new Dictionary<string, string>(), "NONE", 0m, null, null, false, true, false, AuditTimes.Updated),
                    });
                case "/" + WarehouseRoutes.Options:
                    return Json(new List<WarehouseOptionDto> { new(1, "MAIN", "Main Store", "NORMAL", true), new(2, "CONSIGN", "Consignment", "CONSIGNMENT", true) });
                case "/" + UserProfileRoutes.Base:
                    return Json(new UserProfileDto("kim", "Kim", null, null, "ko", 1, "MAIN", "Main Store", AuditTimes.Updated));
                case "/api/codes/PO_STATUS":
                    return Json(new List<CodeItemDto> { new("DR", "작성중"), new("PO", "승인"), new("RE", "부분 입고"), new("CL", "입고 완료"), new("MC", "마감"), new("CN", "취소") });
                case "/" + VendorRoutes.Base:
                    return Json(new VendorListDto([Acme], 1));
                case "/api/mm/vendors/701":
                    return Json(Acme);
                case "/" + PurchasePriceRoutes.Base when query["vendorPrice"] == "ACME":
                    return Json(new List<PurchasePriceDto> { new(1, 11, "BOLT", "Hex Bolt", 9.5m, 0.45m, AuditTimes.Updated) });
                case "/" + ExchangeRateRoutes.Base:
                    var day = DateOnly.Parse(query["from"]!, CultureInfo.InvariantCulture);
                    return Json(new List<ExchangeRateDto> { new(1, day, query["currencyCode"]!, 1382.5m, AuditTimes.Updated) });
                case "/" + ItemRoutes.Base:
                    var code = query["itemCd"];
                    var name = query["itemName"];
                    return Json(new ItemListDto([.. Items.Where(i =>
                        (string.IsNullOrEmpty(code) || i.ItemCd.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                        && (string.IsNullOrEmpty(name) || (i.ItemName ?? "").Contains(name, StringComparison.OrdinalIgnoreCase)))], Items.Length));
                case "/" + PurchaseOrderRoutes.Base when request.Method == HttpMethod.Get:
                    return Json(new List<PurchaseOrderSummaryDto>
                    {
                        new(Order.Id, Order.PoNo, Order.Status, Order.PoDt, Order.VendorId, Order.VendorName, Order.CurrencyCode, Order.TotPoAmt),
                    });
                case "/" + PurchaseOrderRoutes.Base when request.Method == HttpMethod.Post:
                case "/api/mm/purchase-orders/501" when request.Method == HttpMethod.Put:
                    Saved.Add((await request.Content!.ReadFromJsonAsync<SavePurchaseOrderRequest>(cancellationToken))!);
                    if (SaveProblem is { } problem)
                    {
                        SaveProblem = null;
                        return Problem(SaveProblemStatus, problem);
                    }
                    var saved = Saved[^1];
                    return Json(Order with
                    {
                        Lines = [.. saved.Lines.Select((l, i) => new PurchaseOrderLineDto(
                            l.Id ?? 9100 + i, i + 1, l.ItemId, Items.First(x => x.Id == l.ItemId).ItemCd, l.ItemName, l.PoQty, l.PoPrc,
                            l.PoQty * l.PoPrc, l.DcRate, 0m, l.PoQty * l.PoPrc, l.TaxCodeId ?? 1, l.PoQty * l.PoPrc, 0m, l.DueDt, l.WarehouseId, 0m))],
                    });
                case "/api/mm/purchase-orders/501":
                    return Json(Order);
                case "/api/mm/purchase-orders/501/approve":
                    return Json(Order with { Status = "PO", ApprovedAt = AuditTimes.Updated, ApprovedBy = "kim" });
                case "/api/mm/purchase-orders/501/print/purchase-order.pdf":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF-1.7"u8.ToArray()) };
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static ItemDto Item(long id, string code, string name, string type, string unit, decimal price) => new(
            id, code, name, null, null, type, unit, null, "Y", price, 0m, null, null, 0m, null, null, null, null, null, null, true, "TAXABLE",
            "", 0m, 0m, 0m, AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        private static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, null, "application/problem+json") };
    }
}

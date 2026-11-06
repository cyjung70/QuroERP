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
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Contracts.Mm.Vendors;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.Tax;
using QuroErp.Web.Bc.UserProfiles;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.GoodsReceipts;
using QuroErp.Web.Mm.Lots;
using QuroErp.Web.Mm.Vendors;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Goods receipt screen /mm/goods-receipt (MM-UC-004, TASK-MM-008) against a fake API. The company is KR (base KRW).</summary>
public class GoodsReceiptScreenTests : BunitContext
{
    private const string Screen = "goods-receipt";
    private readonly FakeGoodsReceiptApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public GoodsReceiptScreenTests()
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
        Services.AddHttpClient<GoodsReceiptApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<VendorApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LotApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ExchangeRateApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<TaxCodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<UserProfileApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UC-004-T01"), Trait("Rule", "MM-BR-703")]
    public void New_StartsInTheBaseCurrency_WithTheUsersWarehouse_AndNoPayableFields()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=goods-receipt-doc-no]").TextContent.Should().Be("새 문서");
        page.FindAll("[data-testid=goods-receipt-status-chip]").Should().BeEmpty();
        page.Field("goods-receipt-header-currency-code").Value.Should().Be("KRW");
        page.Field("goods-receipt-header-currency-code").IsReadOnly.Should().BeTrue();
        page.Field("goods-receipt-header-warehouse-code").Selected.Should().BeEquivalentTo("MAIN");
        page.Field("goods-receipt-header-warehouse-code").Options.Select(o => o.Code).Should().NotContain("CONSIGN");
        page.FindAll("[data-testid^=goods-receipt-header-]").Select(e => e.GetAttribute("data-testid"))
            .Should().NotContain(id => id!.Contains("due-dt") || id.Contains("terms") || id.Contains("bal") || id.Contains("paid"),
                "the receipt has no payable (MM-BR-751)");
        Enabled(page).Should().BeEquivalentTo("search", "new", "save", "add-row");
        page.Find(".quro-entry-lines-head").TextContent.Should().Contain("거래처를 먼저 선택하세요.");
    }

    [Fact, Trait("Id", "MM-UT-161"), Trait("Rule", "MM-BR-161"), Trait("Rule", "BC-BR-542")]
    public void PickingAVendor_FillsItsCurrencyAndRate_AndEnablesImport()
    {
        var page = RenderAs("admin");

        PickVendor(page);

        page.WaitForAssertion(() => page.Field("goods-receipt-header-exchange-rate").Value.Should().Be("1382.5"));
        page.Field("goods-receipt-header-currency-code").Value.Should().Be("USD");
        Button(page, "import-po").HasAttribute("disabled").Should().BeFalse();
        Button(page, "add-row").Click();
        var added = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (added.WarehouseCode, added.WarehouseId, added.TaxCode, added.TaxCodeId).Should().Be(("MAIN", 1L, "VAT10", 1L),
            "a new line takes the header warehouse and the vendor's tax code");
    }

    [Fact, Trait("Id", "MM-UT-113"), Trait("Rule", "MM-BR-113"), Trait("Rule", "MM-BR-161")]
    public void ImportPo_AddsTheRemainingQuantity_WithThePoCurrencyAndNetPrice_AndSaveSendsThePoLine()
    {
        var page = RenderAs("admin");
        PickVendor(page);
        page.WaitForAssertion(() => page.Field("goods-receipt-header-exchange-rate").Value.Should().Be("1382.5"));

        ImportFirstPoLine(page);

        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        var line = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (line.ItemId, line.GrQty, line.GrPrc, line.GrAmt, line.TaxAmt, line.WarehouseCode, line.PoNo, line.PoSeq)
            .Should().Be((11L, 6m, 9m, 54.00m, 5.40m, "MAIN", "PO2611000001", 1));
        page.Field("goods-receipt-header-exchange-rate").Value.Should().Be("1390", "the rate comes from the PO");
        page.Find("[data-testid=goods-receipt-totals-total]").TextContent.Should().Contain("59.40").And.Contain("USD");

        Button(page, "import-po").Click();
        page.WaitForAssertion(() => page.LookupRows("goods-receipt-po-lookup").Should().BeEmpty("the PO line is on the receipt already"));
        page.Find("[data-testid=goods-receipt-po-lookup]").Should().NotBeNull();
    }

    [Fact, Trait("Id", "MM-UC-004-T01"), Trait("Rule", "MM-BR-114")]
    public void Save_PostsTheLines_AndShowsTheNumber()
    {
        var page = RenderAs("admin");
        PickVendor(page);
        page.WaitForAssertion(() => page.Field("goods-receipt-header-exchange-rate").Value.Should().Be("1382.5"));
        ImportFirstPoLine(page);
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        var request = _api.Saved[0];
        (request.VendorId, request.CurrencyCode, request.ExchangeRate, request.WarehouseId, request.UpdatedAt, request.ConfirmOverReceipt)
            .Should().Be((701L, "USD", 1390m, 1L, (DateTimeOffset?)null, (bool?)null));
        request.Lines.Select(l => (l.Id, l.ItemId, l.GrQty, l.GrPrc, l.TaxCodeId, l.WarehouseId, l.PurchaseOrderLineId))
            .Should().Equal(((long?)null, 11L, 6m, 9m, (long?)1L, (long?)1L, (long?)8001L));
        page.WaitForAssertion(() => page.Find("[data-testid=goods-receipt-doc-no]").TextContent.Should().Be("GR2611000001"));
    }

    [Theory, Trait("Id", "MM-UT-102"), Trait("Rule", "MM-BR-102")]
    [InlineData(true)]
    [InlineData(false)]
    public void OverReceiptWarning_Asks_YesSavesWithTheAnswer_NoDoesNotSave(bool answer)
    {
        var page = Open();
        page.Field("goods-receipt-header-remarks").Change("More than ordered");
        _api.SaveProblem = """{"status":422,"code":"MM-W-102","args":["PO2611000001","1","6"],"confirmField":"confirmOverReceipt"}""";
        _confirm.Answer = answer;

        Button(page, "save").Click();

        page.WaitForAssertion(() => _confirm.Questions.Should().ContainSingle().Which.Should().Contain("PO2611000001"));
        if (answer)
        {
            page.WaitForAssertion(() => _api.Saved.Should().HaveCount(2));
            _api.Saved.Select(s => s.ConfirmOverReceipt).Should().Equal(null, true);
            page.WaitForAssertion(() => page.Find("[data-testid=goods-receipt-message]").TextContent.Should().Be("저장했습니다."));
        }
        else
        {
            page.WaitForAssertion(() => page.Find("[data-testid=goods-receipt-message]").TextContent
                .Should().Be("저장하지 않았습니다. 입고 수량을 확인하세요."));
            _api.Saved.Should().ContainSingle();
        }
    }

    [Fact, Trait("Id", "MM-UT-107"), Trait("Rule", "MM-BR-107"), Trait("Rule", "MM-BR-116"), Trait("Rule", "MM-BR-117")]
    public void SavedReceipt_LocksTheVendorDateAndSavedLines_ButTheLineCanBeDeleted()
    {
        var page = Open();
        var lines = Lines(page);

        page.Field("goods-receipt-header-vendor-name").IsReadOnly.Should().BeTrue();
        Button(page, "import-po").HasAttribute("disabled").Should().BeFalse();
        page.Find("[data-testid=goods-receipt-header-vendor-lookup]").HasAttribute("disabled").Should().BeTrue();
        page.Field("goods-receipt-header-gr-dt").IsReadOnly.Should().BeTrue();
        page.Field("goods-receipt-header-remarks").IsReadOnly.Should().BeFalse("the reference and remarks can change");
        lines.Edit(0, "grQty", "9").Should().BeFalse();
        lines.Select(0);
        Button(page, "mark-delete").HasAttribute("disabled").Should().BeFalse();

        Button(page, "mark-delete").Click();
        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        _api.Saved[0].Lines.Should().BeEmpty("the deleted saved line is left out");
        _api.Saved[0].UpdatedAt.Should().Be(AuditTimes.Updated);
    }

    [Theory, Trait("Id", "MM-UT-105"), Trait("Rule", "MM-BR-105"), Trait("Rule", "MM-BR-104")]
    [InlineData("OP", 0, "brand", "미대사", "save,add-row,mark-delete,import-po,delete")]
    [InlineData("PA", 3, "warn", "부분 대사", "")]
    [InlineData("CL", 6, "ok", "대사 완료", "")]
    [InlineData("VO", 0, "danger", "무효", "")]
    public void Status_SetsTheChipAndTheEnabledButtons(string status, int invoiced, string chip, string text, string enabled)
    {
        _api.Receipt = Receipt(status, invoiced);

        var page = Open();

        page.Find("[data-testid=goods-receipt-status-chip] .quro-chip").ClassList.Should().Contain($"quro-chip-{chip}");
        page.Find("[data-testid=goods-receipt-status-chip]").TextContent.Should().Be(text);
        Enabled(page).Except(["search", "new"]).Should().BeEquivalentTo(enabled.Split(',', StringSplitOptions.RemoveEmptyEntries));
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Contain(["goods-receipt-toolbar-import-po", "goods-receipt-toolbar-delete"], "disabled buttons stay");
    }

    [Fact, Trait("Id", "MM-UT-353"), Trait("Rule", "MM-BR-353"), Trait("Rule", "MM-BR-362")]
    public async Task LotItem_TakesATypedLot_OrAnExistingOne_AndOtherItemsHaveNone()
    {
        var page = RenderAs("admin");
        PickVendor(page);
        page.WaitForAssertion(() => page.Field("goods-receipt-header-exchange-rate").Value.Should().Be("1382.5"));
        var lines = Lines(page);
        Button(page, "add-row").Click();
        (await lines.EditAsync(0, "itemCd", "resin")).Should().BeTrue();
        await lines.EditAsync(0, "grQty", "5");
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(lines.KeyOf(0)).LotManaged.Should().BeTrue());

        lines.Select(0);
        var picking = lines.F4Async(0, "lotNo", "l-0");
        page.WaitForAssertion(() => page.LookupRows("goods-receipt-line-lookup").Should().HaveCount(2));
        page.LookupRows("goods-receipt-line-lookup").Select(r => r.TextContent).Should()
            .SatisfyRespectively(r => r.Should().Contain("L-0"), r => r.Should().Contain("L-01").And.Contain("4"));
        page.PickLookupRow<LookupRow, GoodsReceipt>("goods-receipt-line-lookup", 1);
        await picking;
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(lines.KeyOf(0)).LotNo.Should().Be("L-01"));
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/mm/lots" && r.RequestUri.Query.Contains("itemId=14", StringComparison.Ordinal));

        Button(page, "add-row").Click();
        (await lines.EditAsync(1, "itemCd", "bolt")).Should().BeTrue();
        await lines.EditAsync(1, "grQty", "1");
        await lines.EditAsync(1, "lotNo", "X1");
        Frame(page).Lines.GetItem(lines.KeyOf(1)).LotNo.Should().BeNull("only a lot-managed item has a lot");

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        _api.Saved[0].Lines.Select(l => (l.ItemId, l.LotNo)).Should().Equal((14L, "L-01"), (11L, (string?)null));
    }

    [Fact, Trait("Id", "MM-UT-112"), Trait("Rule", "MM-BR-112")]
    public async Task ChangingQuantityOrPrice_CalculatesTheDisplayAmountAndTax()
    {
        var page = RenderAs("admin");
        PickVendor(page);
        page.WaitForAssertion(() => page.Field("goods-receipt-header-exchange-rate").Value.Should().Be("1382.5"));
        var lines = Lines(page);
        Button(page, "add-row").Click();
        (await lines.EditAsync(0, "itemCd", "bolt")).Should().BeTrue();

        await lines.EditAsync(0, "grQty", "3");
        await lines.EditAsync(0, "grPrc", "2.5");

        var line = Frame(page).Lines.GetItem(lines.KeyOf(0));
        (line.ItemId, line.GrAmt, line.TaxAmt, line.Unit).Should().Be((11L, 7.50m, 0.75m, "EA"));
        Frame(page).Lines.GetState(lines.KeyOf(0)).Should().Be(RowState.Inserted);
    }

    [Fact, Trait("Id", "MM-UC-004-T04")]
    public void SaveAfterSomeoneElseSaved_Shows409Message()
    {
        var page = Open();
        page.Field("goods-receipt-header-remarks").Change("Late");
        _api.SaveProblem = """{"status":409,"code":"Common-E-004"}""";
        _api.SaveProblemStatus = HttpStatusCode.Conflict;

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=goods-receipt-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Rule", "BC-BR-214")]
    public void UserWithoutFullAccess_CanOnlyFind()
    {
        var page = RenderAs("user");

        Button(page, "save").HasAttribute("disabled").Should().BeTrue();
        Button(page, "add-row").HasAttribute("disabled").Should().BeTrue();
        Button(page, "import-po").HasAttribute("disabled").Should().BeTrue();
        page.Field("goods-receipt-header-remarks").IsReadOnly.Should().BeTrue();
    }

    [Fact, Trait("Id", "MM-UC-004-T01")]
    public void Labels_AreTheEnglishResources_InEnglish()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = Open();

        page.Find("[data-testid=goods-receipt-title] h1").TextContent.Should().Be("Goods Receipt");
        page.Find("[data-testid=goods-receipt-status-chip]").TextContent.Should().Be("Open");
        Button(page, "import-po").TextContent.Should().Be("Import PO Lines");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["Item Code", "Lot", "Unit Price", "PO No.", "Received"]);
        page.Find("[data-testid=goods-receipt-totals-total]").TextContent.Should().Contain("59.40");
    }

    private IRenderedComponent<GoodsReceipt> RenderAs(string role, string? grNo = null)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<GoodsReceipt>(p => p.Add(x => x.GrNo, grNo));
        page.WaitForElement("[data-testid=goods-receipt-header-vendor-name]");
        return page;
    }

    private IRenderedComponent<GoodsReceipt> Open()
    {
        var page = RenderAs("admin", "GR2611000001");
        page.WaitForAssertion(() => page.Find("[data-testid=goods-receipt-doc-no]").TextContent.Should().Be("GR2611000001"));
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        return page;
    }

    private static void PickVendor(IRenderedComponent<GoodsReceipt> page)
    {
        page.Find("[data-testid=goods-receipt-header-vendor-lookup]").Click();
        page.WaitForAssertion(() => page.LookupRows("goods-receipt-vendor-lookup").Should().NotBeEmpty());
        page.PickLookupRow<VendorDto, GoodsReceipt>("goods-receipt-vendor-lookup", 0);
    }

    private static void ImportFirstPoLine(IRenderedComponent<GoodsReceipt> page)
    {
        Button(page, "import-po").Click();
        page.WaitForAssertion(() => page.LookupRows("goods-receipt-po-lookup").Should().ContainSingle());
        page.PickLookupRow<OpenPoLineDto, GoodsReceipt>("goods-receipt-po-lookup", 0);
    }

    private static DataGridPage<GoodsReceiptLine> Lines(IRenderedComponent<GoodsReceipt> page) =>
        page.DataGrid<GoodsReceiptLine, GoodsReceipt>("goods-receipt-line");

    private static QuroHeaderDetail<GoodsReceiptHeader, GoodsReceiptLine> Frame(IRenderedComponent<GoodsReceipt> page) =>
        page.FindComponent<QuroHeaderDetail<GoodsReceiptHeader, GoodsReceiptLine>>().Instance;

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<GoodsReceipt> page, string action) =>
        page.Find($"[data-testid={Screen}-toolbar-{action}]");

    private static IReadOnlyList<string> Enabled(IRenderedComponent<GoodsReceipt> page) =>
        [.. page.FindAll("[role=toolbar] button:not([disabled])").Select(b => b.GetAttribute("data-testid")!.Replace($"{Screen}-toolbar-", ""))];

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    // One line of 6 × 9 USD from PO line 8001, VAT 10 %
    private static GoodsReceiptDto Receipt(string status = "OP", decimal invoiced = 0) => new(
        601, "GR2611000001", status, 701, "ACME", new DateOnly(2026, 11, 4), 1, "INV-77", "Dock 2", "USD", 1390m,
        54m, 5.4m, 59.4m, 82566m,
        [new GoodsReceiptLineDto(9501, 1, 11, "BOLT", "Hex Bolt", "EA", 1, "MAIN", null, null, 6m, 9m, 54m, 1, "VAT10", 54m, 5.4m, true,
            8001, "PO2611000001", 1, 10m, 10m, invoiced, 0m)],
        AuditTimes.Updated, AuditTimes.Created, "setup", "kim");

    /// <summary>Goods receipt, vendor, item, lot, rate, localization, tax code, warehouse and profile API.</summary>
    private sealed class FakeGoodsReceiptApi : HttpMessageHandler
    {
        private static readonly VendorDto Acme = new(701, "ACME", "Kim", null, "555-0100", null, null, null, null, "NET_30", "Y", 0m,
            null, null, null, null, null, "USA", null, null, null, null, null, null, null,
            "ACME", "USD", 1, "VAT10", null, null, null, null, null, AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

        private static readonly ItemDto[] Items =
        [
            Item(11, "BOLT", "Hex Bolt", "INVENTORY", "EA", 0.45m),
            Item(12, "FREIGHT", "Freight", "SERVICE", "LOT", 75m),
            Item(13, "KIT", "Kit", "SET", "EA", 10m),
            Item(14, "RESIN", "Resin", "INVENTORY", "KG", 3.2m, lotManaged: true),
        ];

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<SaveGoodsReceiptRequest> Saved { get; } = [];

        public GoodsReceiptDto Receipt { get; set; } = GoodsReceiptScreenTests.Receipt();

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
                case "/" + VendorRoutes.Base:
                    return Json(new VendorListDto([Acme], 1));
                case "/api/mm/vendors/701":
                    return Json(Acme);
                case "/" + ExchangeRateRoutes.Base:
                    var day = DateOnly.Parse(query["from"]!, CultureInfo.InvariantCulture);
                    return Json(new List<ExchangeRateDto> { new(1, day, query["currencyCode"]!, 1382.5m, AuditTimes.Updated) });
                case "/" + ItemRoutes.Base:
                    var code = query["itemCd"];
                    var name = query["itemName"];
                    return Json(new ItemListDto([.. Items.Where(i =>
                        (string.IsNullOrEmpty(code) || i.ItemCd.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                        && (string.IsNullOrEmpty(name) || (i.ItemName ?? "").Contains(name, StringComparison.OrdinalIgnoreCase)))], Items.Length));
                case "/" + LotRoutes.Base:
                    return Json(new List<LotBalanceDto> { new(31, "L-01", 4m), new(32, "M-02", 2m) });
                case "/api/mm/goods-receipts/open-po-lines":
                    return Json(new List<OpenPoLineDto>
                    {
                        new(8001, 501, "PO2611000001", 1, new DateOnly(2026, 11, 1), 11, "BOLT", "Hex Bolt", "EA", 1, 10m, 4m, 6m,
                            10m, 10m, 1, "USD", 1390m),
                    });
                case "/" + GoodsReceiptRoutes.Base when request.Method == HttpMethod.Get:
                    return Json(new List<GoodsReceiptSummaryDto>
                    {
                        new(Receipt.Id, Receipt.GrNo, Receipt.Status, Receipt.GrDt, Receipt.VendorId, Receipt.VendorName, Receipt.CurrencyCode, Receipt.TotGrAmt),
                    });
                case "/" + GoodsReceiptRoutes.Base when request.Method == HttpMethod.Post:
                case "/api/mm/goods-receipts/601" when request.Method == HttpMethod.Put:
                    Saved.Add((await request.Content!.ReadFromJsonAsync<SaveGoodsReceiptRequest>(cancellationToken))!);
                    if (SaveProblem is { } problem)
                    {
                        SaveProblem = null;
                        return Problem(SaveProblemStatus, problem);
                    }
                    var saved = Saved[^1];
                    return Json(Receipt with
                    {
                        Lines = [.. saved.Lines.Select((l, i) => new GoodsReceiptLineDto(
                            l.Id ?? 9600 + i, i + 1, l.ItemId, Items.First(x => x.Id == l.ItemId).ItemCd, l.ItemName, "EA", l.WarehouseId ?? 1, "MAIN",
                            l.LotNo is null ? null : 40 + i, l.LotNo, l.GrQty, l.GrPrc, l.GrQty * l.GrPrc, l.TaxCodeId ?? 1, "VAT10",
                            l.GrQty * l.GrPrc, 0m, l.Deductible, l.PurchaseOrderLineId, null, null, null, null, 0m, 0m))],
                    });
                case "/api/mm/goods-receipts/601":
                    return Json(Receipt);
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static ItemDto Item(long id, string code, string name, string type, string unit, decimal price, bool lotManaged = false) => new(
            id, code, name, null, null, type, unit, null, "Y", price, 0m, null, null, 0m, null, null, null, null, null, null, true, "TAXABLE",
            "", 0m, 0m, 0m, AuditTimes.Created, "setup", AuditTimes.Updated, "kim", lotManaged, lotManaged ? new DateOnly(2026, 1, 1) : null);

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        private static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, null, "application/problem+json") };
    }
}

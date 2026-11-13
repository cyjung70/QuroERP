using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quro.Grid.Data;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Contracts.Mm.SupplierInvoices;
using QuroErp.Contracts.Mm.Vendors;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.Tax;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.SupplierInvoices;
using QuroErp.Web.Mm.Vendors;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Supplier invoice screen /mm/supplier-invoice (MM-UC-032, TASK-MM-048) against a fake API. The company is KR (base KRW).</summary>
public class SupplierInvoiceScreenTests : BunitContext
{
    private const string Screen = "supplier-invoice";
    private readonly FakeSupplierInvoiceApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public SupplierInvoiceScreenTests()
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
        Services.AddHttpClient<SupplierInvoiceApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<VendorApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ExchangeRateApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<TaxCodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UC-032-T01"), Trait("Rule", "MM-BR-756"), Trait("Rule", "MM-BR-763")]
    public void New_IsDueOnTheInvoiceDate_AndLinesOnlyComeFromReceipts()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=supplier-invoice-doc-no]").TextContent.Should().Be("새 문서");
        page.FindAll("[data-testid=supplier-invoice-status-chip]").Should().BeEmpty();
        page.Field("supplier-invoice-header-currency-code").Value.Should().Be("KRW");
        page.Field("supplier-invoice-header-currency-code").IsReadOnly.Should().BeTrue();
        page.Field("supplier-invoice-header-due-dt").Value.Should().Be(page.Field("supplier-invoice-header-invoice-dt").Value);
        page.Field("supplier-invoice-header-bal-amt").IsReadOnly.Should().BeTrue();
        Enabled(page).Should().BeEquivalentTo("search", "new", "save");
        Button(page, "add-row").HasAttribute("disabled").Should().BeTrue("a line must match a receipt line");
        Button(page, "import-receipts").HasAttribute("disabled").Should().BeTrue("the vendor comes first");
        page.Find(".quro-entry-lines-head").TextContent.Should().Contain("거래처를 먼저 선택하세요.");
    }

    [Fact, Trait("Id", "MM-UT-756"), Trait("Rule", "MM-BR-756")]
    public void PickingAVendor_FillsItsTermsCurrencyAndRate_AndEnablesImport()
    {
        var page = RenderAs("admin");

        PickVendor(page);

        page.WaitForAssertion(() => page.Field("supplier-invoice-header-exchange-rate").Value.Should().Be("1382.5"));
        page.Field("supplier-invoice-header-currency-code").Value.Should().Be("USD");
        Frame(page).Header.Terms.Should().Be("NET_30");
        Button(page, "import-receipts").HasAttribute("disabled").Should().BeFalse();
        Button(page, "add-row").HasAttribute("disabled").Should().BeTrue();
        page.Find(".quro-entry-lines-head").TextContent.Should().Contain("입고 가져오기로 줄을 넣으세요.");
    }

    [Fact, Trait("Id", "MM-UC-032-T01"), Trait("Rule", "MM-BR-751"), Trait("Rule", "MM-BR-753"), Trait("Rule", "MM-BR-763")]
    public void ImportFromTwoReceipts_AddsTheOpenQuantities_AndOffersOnlyLinesNotTaken()
    {
        var page = RenderAs("admin");
        PickVendor(page);
        page.WaitForAssertion(() => page.Field("supplier-invoice-header-exchange-rate").Value.Should().Be("1382.5"));

        ImportReceiptLine(page, 0, 2);
        ImportReceiptLine(page, 0, 1);

        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(2));
        var lines = Frame(page).Lines;
        var all = lines.RowKeys.Select(lines.GetItem).OrderBy(l => l.GrNo).ToList();
        all.Select(l => (l.GoodsReceiptLineId, l.GrNo, l.Qty, l.Prc, l.SupplyAmt, l.TaxAmt, l.TaxCode)).Should().Equal(
            ((long?)9501, "GR2611000001", (decimal?)6m, (decimal?)9m, (decimal?)54.00m, (decimal?)5.40m, "VAT10"),
            ((long?)9502, "GR2611000002", (decimal?)5m, (decimal?)3.2m, (decimal?)16.00m, (decimal?)1.60m, "VAT10"));
        page.Find("[data-testid=supplier-invoice-totals-total]").TextContent.Should().Contain("77.00").And.Contain("USD");
        _api.OpenLineQueries.Should().HaveCount(2);
        _api.OpenLineQueries[0].Should().Contain("vendorId=701").And.NotContain("currencyCode");
        _api.OpenLineQueries[1].Should().Contain("currencyCode=USD", "lines fix the invoice currency (MM-BR-756)");

        Button(page, "import-receipts").Click();
        page.WaitForAssertion(() => page.LookupRows("supplier-invoice-receipt-lookup").Should().BeEmpty("both lines are on the invoice"));
    }

    [Fact, Trait("Id", "MM-UC-032-T01"), Trait("Rule", "MM-BR-752")]
    public void Save_PostsTheMatchedLines_AndShowsTheNumber()
    {
        var page = RenderAs("admin");
        PickVendor(page);
        page.WaitForAssertion(() => page.Field("supplier-invoice-header-exchange-rate").Value.Should().Be("1382.5"));
        ImportReceiptLine(page, 0, 2);
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        page.Field("supplier-invoice-header-ref-no").Change("inv-77");
        page.Find("[data-testid=supplier-invoice-header-tax-invoice-received]").Change(true);

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        var request = _api.Saved[0];
        (request.VendorId, request.RefNo, request.Terms, request.ExchangeRate, request.TaxInvoiceReceived, request.UpdatedAt,
                request.ConfirmPriceDifference)
            .Should().Be((701L, "INV-77", "NET_30", (decimal?)1382.5m, true, (DateTimeOffset?)null, (bool?)null));
        request.DueDt.Should().Be(request.InvoiceDt);
        request.Lines.Select(l => (l.Id, l.GoodsReceiptLineId, l.Qty, l.Prc, l.TaxCodeId, l.Deductible))
            .Should().Equal(((long?)null, 9501L, 6m, (decimal?)9m, (long?)1L, (bool?)true));
        page.WaitForAssertion(() => page.Find("[data-testid=supplier-invoice-doc-no]").TextContent.Should().Be("VI2611000001"));
    }

    [Theory, Trait("Id", "MM-UC-032-T03"), Trait("Rule", "MM-BR-754")]
    [InlineData(true)]
    [InlineData(false)]
    public void PriceDifferenceWarning_Asks_YesSavesWithTheAnswer_NoDoesNotSave(bool answer)
    {
        var page = Open();
        page.Field("supplier-invoice-header-remarks").Change("Price up");
        _api.SaveProblem = """{"status":422,"code":"MM-W-754","args":["1","9.5","9"],"confirmField":"confirmPriceDifference"}""";
        _confirm.Answer = answer;

        Button(page, "save").Click();

        page.WaitForAssertion(() => _confirm.Questions.Should().ContainSingle().Which.Should().Contain("9.5"));
        if (answer)
        {
            page.WaitForAssertion(() => _api.Saved.Should().HaveCount(2));
            _api.Saved.Select(s => s.ConfirmPriceDifference).Should().Equal(null, true);
            page.WaitForAssertion(() => page.Find("[data-testid=supplier-invoice-message]").TextContent.Should().Be("저장했습니다."));
        }
        else
        {
            page.WaitForAssertion(() => page.Find("[data-testid=supplier-invoice-message]").TextContent
                .Should().Be("저장하지 않았습니다. 송장 단가를 확인하세요."));
            _api.Saved.Should().ContainSingle();
        }
    }

    [Fact, Trait("Id", "MM-UT-760"), Trait("Rule", "MM-BR-760")]
    public void SavedInvoice_LocksTheVendorDateAndSavedLines_ButTheLineCanBeDeletedAndImportedAgain()
    {
        var page = Open();
        var lines = Lines(page);

        page.Field("supplier-invoice-header-vendor-name").IsReadOnly.Should().BeTrue();
        page.Find("[data-testid=supplier-invoice-header-vendor-lookup]").HasAttribute("disabled").Should().BeTrue();
        page.Field("supplier-invoice-header-invoice-dt").IsReadOnly.Should().BeTrue("the invoice has saved lines");
        page.Field("supplier-invoice-header-exchange-rate").IsReadOnly.Should().BeTrue("the invoice has saved lines");
        page.Field("supplier-invoice-header-due-dt").IsReadOnly.Should().BeFalse();
        page.Field("supplier-invoice-header-remarks").IsReadOnly.Should().BeFalse();
        lines.Edit(0, "qty", "3").Should().BeFalse();
        lines.Select(0);

        Button(page, "mark-delete").Click();
        Button(page, "import-receipts").Click();
        page.WaitForAssertion(() => page.LookupRows("supplier-invoice-receipt-lookup").Should().HaveCount(2, "the deleted line can come back"));
        _api.OpenLineQueries[^1].Should().Contain("excludeInvoiceId=801");
        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        _api.Saved[0].Lines.Should().BeEmpty("the deleted saved line is left out");
        _api.Saved[0].UpdatedAt.Should().Be(AuditTimes.Updated);
        _api.Saved[0].ExchangeRate.Should().Be(1390m, "the stored rate stays");
    }

    [Theory, Trait("Id", "MM-UT-761"), Trait("Rule", "MM-BR-761"), Trait("Rule", "MM-BR-760"), Trait("Rule", "MM-BR-762")]
    [InlineData("INV", "OP", 0, "brand", "미지급", "save,mark-delete,import-receipts,delete")]
    [InlineData("INV", "PA", 10, "warn", "부분 지급", "")]
    [InlineData("INV", "CL", 59.4, "ok", "지급 완료", "")]
    [InlineData("INV", "VO", 0, "danger", "무효", "")]
    [InlineData("DM", "OP", 0, "brand", "미지급", "")]
    public void StatusAndType_SetTheChipAndTheEnabledButtons(string type, string status, double paid, string chip, string text, string enabled)
    {
        _api.Invoice = Invoice(type, status, (decimal)paid);

        var page = Open();

        page.Find("[data-testid=supplier-invoice-status-chip] .quro-chip").ClassList.Should().Contain($"quro-chip-{chip}");
        page.Find("[data-testid=supplier-invoice-status-chip]").TextContent.Should().Be(text);
        page.Field("supplier-invoice-header-type").Value.Should().Be(type == "DM" ? "Debit Memo" : "송장");
        Enabled(page).Except(["search", "new"]).Should().BeEquivalentTo(enabled.Split(',', StringSplitOptions.RemoveEmptyEntries));
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Contain(["supplier-invoice-line-import-receipts", "supplier-invoice-toolbar-delete", "supplier-invoice-line-add-row"],
                "disabled buttons stay");
    }

    [Theory, Trait("Id", "MM-UC-032-T05"), Trait("Rule", "MM-BR-760"), Trait("Rule", "MM-BR-762")]
    [InlineData("INV", 10)]
    [InlineData("DM", 0)]
    public void PaidInvoiceOrDebitMemo_HasEveryFieldReadOnly(string type, double paid)
    {
        _api.Invoice = Invoice(type, paid > 0 ? "PA" : "OP", (decimal)paid);

        var page = Open();

        foreach (var field in new[] { "ref-no", "due-dt", "exchange-rate", "remarks", "invoice-dt", "vendor-name" })
        {
            page.Field($"supplier-invoice-header-{field}").IsReadOnly.Should().BeTrue(field);
        }
        page.Field("supplier-invoice-header-cls-amt").Value.Should().Be(paid > 0 ? "10.00" : "0.00");
        Lines(page).Edit(0, "qty", "3").Should().BeFalse();
        foreach (var command in new[] { "save", "delete", "add-row", "mark-delete", "import-receipts" })
        {
            Button(page, command).HasAttribute("disabled").Should().BeTrue(command);
        }
    }

    [Fact, Trait("Id", "MM-UT-756"), Trait("Rule", "MM-BR-756"), Trait("Rule", "MM-BR-760")]
    public void ExchangeRate_CanChangeUntilALineIsSaved()
    {
        var page = RenderAs("admin");
        PickVendor(page);
        page.WaitForAssertion(() => page.Field("supplier-invoice-header-exchange-rate").Value.Should().Be("1382.5"));
        page.Field("supplier-invoice-header-exchange-rate").IsReadOnly.Should().BeFalse("no line is saved yet");
        ImportReceiptLine(page, 0, 2);
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        page.Field("supplier-invoice-header-exchange-rate").IsReadOnly.Should().BeFalse("the line is not saved yet");

        page.Field("supplier-invoice-header-exchange-rate").Change("1400");
        page.Field("supplier-invoice-header-ref-no").Change("inv-78");
        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        _api.Saved[0].ExchangeRate.Should().Be(1400m);
        page.WaitForAssertion(() => page.Field("supplier-invoice-header-exchange-rate").IsReadOnly.Should().BeTrue("a line is saved"));
        page.Field("supplier-invoice-header-invoice-dt").IsReadOnly.Should().BeTrue();
    }

    [Fact, Trait("Id", "MM-UC-032-T01")]
    public void OpenedWithAnUnknownNumber_ShowsANewInvoice_AndNoDataMessage()
    {
        var page = RenderAs("admin", "VI2611999999");

        page.WaitForAssertion(() => page.Find("[data-testid=supplier-invoice-message]").TextContent.Should().Be("조회된 내용이 없습니다."));
        page.Find("[data-testid=supplier-invoice-doc-no]").TextContent.Should().Be("새 문서");
        _api.Searches.Should().ContainSingle().Which.Should().Contain("invoiceNo=VI2611999999");
    }

    [Fact, Trait("Id", "MM-UC-032-T01")]
    public void Find_SendsNumberVendorPeriodTypeAndStatus()
    {
        var page = RenderAs("admin");
        Button(page, "search").Click();
        page.WaitForAssertion(() => page.LookupRows("supplier-invoice-find").Should().ContainSingle());

        page.Find("[data-testid=supplier-invoice-find-keyword]").Change("vi2611");
        page.Find("[data-testid=supplier-invoice-find-vendor]").Change("acme");
        page.Find("[data-testid=supplier-invoice-find-from]").Change("2026-11-01");
        page.Find("[data-testid=supplier-invoice-find-to]").Change("2026-11-30");
        page.Find("[data-testid=supplier-invoice-find-type]").Change("INV");
        page.Find("[data-testid=supplier-invoice-find-status]").Change("OP");
        page.Find("form.quro-search").Submit();

        page.WaitForAssertion(() => _api.Searches[^1].Should().Contain("invoiceNo=VI2611").And.Contain("vendorId=701")
            .And.Contain("fromDt=2026-11-01").And.Contain("toDt=2026-11-30").And.Contain("type=INV").And.Contain("status=OP"));
        page.LookupRows("supplier-invoice-find").Should().ContainSingle();

        page.Find("[data-testid=supplier-invoice-find-vendor]").Change("nobody");
        page.Find("form.quro-search").Submit();

        page.WaitForAssertion(() => page.LookupRows("supplier-invoice-find").Should().BeEmpty("no invoice has this vendor"));
        _api.Searches[^1].Should().NotContain("vendorId");
    }

    [Fact, Trait("Id", "MM-UT-755"), Trait("Rule", "MM-BR-755")]
    public async Task ChangingQuantityPriceOrTaxCode_CalculatesTheDisplayAmountAndTax()
    {
        var page = RenderAs("admin");
        PickVendor(page);
        page.WaitForAssertion(() => page.Field("supplier-invoice-header-exchange-rate").Value.Should().Be("1382.5"));
        ImportReceiptLine(page, 0, 2);
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        var lines = Lines(page);

        await lines.EditAsync(0, "qty", "4");
        await lines.EditAsync(0, "prc", "9.5");

        var line = Frame(page).Lines.GetItem(lines.KeyOf(0));
        (line.SupplyAmt, line.TaxAmt).Should().Be((38.00m, 3.80m));
        (await lines.EditAsync(0, "taxCode", "none")).Should().BeTrue();
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(lines.KeyOf(0)).TaxCodeId.Should().Be(2));
        Frame(page).Lines.GetItem(lines.KeyOf(0)).TaxAmt.Should().Be(0m);
        Frame(page).Lines.GetState(lines.KeyOf(0)).Should().Be(RowState.Inserted);
    }

    [Fact, Trait("Id", "MM-UC-032-T05")]
    public void SaveAfterSomeoneElseSaved_Shows409Message()
    {
        var page = Open();
        page.Field("supplier-invoice-header-remarks").Change("Late");
        _api.SaveProblem = """{"status":409,"code":"Common-E-004"}""";
        _api.SaveProblemStatus = HttpStatusCode.Conflict;

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=supplier-invoice-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Rule", "BC-BR-214")]
    public void UserWithoutFullAccess_CanOnlyFind()
    {
        var page = RenderAs("user");

        Button(page, "save").HasAttribute("disabled").Should().BeTrue();
        Button(page, "import-receipts").HasAttribute("disabled").Should().BeTrue();
        page.Field("supplier-invoice-header-remarks").IsReadOnly.Should().BeTrue();
    }

    [Fact, Trait("Id", "MM-UC-032-T01")]
    public void Labels_AreTheEnglishResources_InEnglish()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = Open();

        page.Find("[data-testid=supplier-invoice-title] h1").TextContent.Should().Be("Supplier Invoice");
        page.Find("[data-testid=supplier-invoice-status-chip]").TextContent.Should().Be("Open");
        Button(page, "import-receipts").TextContent.Should().Be("Import Receipts");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["GR No.", "Received Qty", "Unit Price", "Deductible", "Price Variance"]);
        page.Find("[data-testid=supplier-invoice-totals-total]").TextContent.Should().Contain("59.40");
    }

    private IRenderedComponent<SupplierInvoice> RenderAs(string role, string? invoiceNo = null)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<SupplierInvoice>(p => p.Add(x => x.InvoiceNo, invoiceNo));
        page.WaitForElement("[data-testid=supplier-invoice-header-vendor-name]");
        return page;
    }

    private IRenderedComponent<SupplierInvoice> Open()
    {
        var page = RenderAs("admin", "VI2611000001");
        page.WaitForAssertion(() => page.Find("[data-testid=supplier-invoice-doc-no]").TextContent.Should().Be("VI2611000001"));
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        return page;
    }

    private static void PickVendor(IRenderedComponent<SupplierInvoice> page)
    {
        page.Find("[data-testid=supplier-invoice-header-vendor-lookup]").Click();
        page.WaitForAssertion(() => page.LookupRows("supplier-invoice-vendor-lookup").Should().NotBeEmpty());
        page.PickLookupRow<VendorDto, SupplierInvoice>("supplier-invoice-vendor-lookup", 0);
    }

    private static void ImportReceiptLine(IRenderedComponent<SupplierInvoice> page, int row, int offered)
    {
        Button(page, "import-receipts").Click();
        page.WaitForAssertion(() => page.LookupRows("supplier-invoice-receipt-lookup").Should().HaveCount(offered));
        page.PickLookupRow<OpenReceiptLineDto, SupplierInvoice>("supplier-invoice-receipt-lookup", row);
    }

    private static DataGridPage<SupplierInvoiceLine> Lines(IRenderedComponent<SupplierInvoice> page) =>
        page.DataGrid<SupplierInvoiceLine, SupplierInvoice>("supplier-invoice-line");

    private static QuroHeaderDetail<SupplierInvoiceHeader, SupplierInvoiceLine> Frame(IRenderedComponent<SupplierInvoice> page) =>
        page.FindComponent<QuroHeaderDetail<SupplierInvoiceHeader, SupplierInvoiceLine>>().Instance;

    private static readonly string[] LineCommands = ["add-row", "mark-delete", "restore-row", "import-receipts"];

    // Row commands sit on the line panel (entry-frame.md 2), the rest on the command bar
    private static AngleSharp.Dom.IElement Button(IRenderedComponent<SupplierInvoice> page, string action) =>
        page.Find($"[data-testid={Screen}-{(LineCommands.Contains(action) ? "line" : "toolbar")}-{action}]");

    private static IReadOnlyList<string> Enabled(IRenderedComponent<SupplierInvoice> page) =>
        [.. page.FindAll("[role=toolbar] button:not([disabled])")
            .Select(b => b.GetAttribute("data-testid")!.Replace($"{Screen}-toolbar-", "").Replace($"{Screen}-line-", ""))];

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    // One line of 6 × 9 USD matching receipt line 9501, VAT 10 %
    private static SupplierInvoiceDto Invoice(string type = "INV", string status = "OP", decimal paid = 0m) => new(
        801, "VI2611000001", type, status, 701, "ACME", "INV-77", new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 5), "NET_30", "USD", 1390m,
        true, "Nov", 54m, 5.4m, 59.4m, 82566m, paid, 59.4m - paid, type == "DM" ? 3001 : null,
        [new SupplierInvoiceLineDto(9701, 1, 9501, "GR2611000001", 1, 11, "BOLT", "Hex Bolt", "EA", 6m, 9m, 6m, 9m, 1, "VAT10", 54m, 5.4m, true, 0m)],
        AuditTimes.Updated, AuditTimes.Created, "setup", "kim");

    /// <summary>Supplier invoice, vendor, rate, localization, tax code and code API.</summary>
    private sealed class FakeSupplierInvoiceApi : HttpMessageHandler
    {
        private static readonly VendorDto Acme = new(701, "ACME", "Kim", null, "555-0100", null, null, null, null, "NET_30", "Y", 0m,
            null, null, null, null, null, "USA", null, null, null, null, null, null, null,
            "ACME", "USD", 1, "VAT10", null, null, null, null, null, AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

        private static readonly OpenReceiptLineDto[] OpenLines =
        [
            new(9501, 601, "GR2611000001", 1, new DateOnly(2026, 11, 4), 11, "BOLT", "Hex Bolt", "EA", 10m, 0m, 4m, 6m, 9m, 9m, 1, "VAT10",
                true, "USD", 1390m),
            new(9502, 602, "GR2611000002", 1, new DateOnly(2026, 11, 5), 14, "RESIN", "Resin", "KG", 5m, 0m, 0m, 5m, 3.2m, null, 1, "VAT10",
                true, "USD", 1390m),
        ];

        public List<string> OpenLineQueries { get; } = [];

        public List<string> Searches { get; } = [];

        public List<SaveSupplierInvoiceRequest> Saved { get; } = [];

        public SupplierInvoiceDto Invoice { get; set; } = SupplierInvoiceScreenTests.Invoice();

        /// <summary>The body of the next save's error; the save after it passes.</summary>
        public string? SaveProblem { get; set; }

        public HttpStatusCode SaveProblemStatus { get; set; } = HttpStatusCode.UnprocessableEntity;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
                case "/" + CodeRoutes.Base + "/TERMS":
                    return Json(new List<CodeItemDto> { new("NET_30", "Net 30"), new("COD_15", "COD 15") });
                case "/" + VendorRoutes.Base:
                    return Json(new VendorListDto([Acme], 1));
                case "/api/mm/vendors/701":
                    return Json(Acme);
                case "/" + ExchangeRateRoutes.Base:
                    var day = DateOnly.Parse(query["from"]!, CultureInfo.InvariantCulture);
                    return Json(new List<ExchangeRateDto> { new(1, day, query["currencyCode"]!, 1382.5m, AuditTimes.Updated) });
                case "/api/mm/supplier-invoices/open-receipt-lines":
                    OpenLineQueries.Add(request.RequestUri.Query);
                    return Json(OpenLines.Where(l => query["currencyCode"] is not { } c || l.CurrencyCode == c).ToList());
                case "/" + SupplierInvoiceRoutes.Base when request.Method == HttpMethod.Get:
                    Searches.Add(request.RequestUri.Query);
                    return Json(new List<SupplierInvoiceSummaryDto>
                    {
                        new(Invoice.Id, Invoice.InvoiceNo, Invoice.Type, Invoice.Status, Invoice.InvoiceDt, Invoice.DueDt, Invoice.VendorId,
                            Invoice.VendorName, Invoice.RefNo, Invoice.CurrencyCode, Invoice.TotAmt, Invoice.BalAmt),
                    });
                case "/" + SupplierInvoiceRoutes.Base when request.Method == HttpMethod.Post:
                case "/api/mm/supplier-invoices/801" when request.Method == HttpMethod.Put:
                    Saved.Add((await request.Content!.ReadFromJsonAsync<SaveSupplierInvoiceRequest>(cancellationToken))!);
                    if (SaveProblem is { } problem)
                    {
                        SaveProblem = null;
                        return Problem(SaveProblemStatus, problem);
                    }
                    var saved = Saved[^1];
                    return Json(Invoice with
                    {
                        RefNo = saved.RefNo ?? Invoice.RefNo,
                        Lines = [.. saved.Lines.Select((l, i) =>
                        {
                            var open = OpenLines.First(o => o.GoodsReceiptLineId == l.GoodsReceiptLineId);
                            return new SupplierInvoiceLineDto(l.Id ?? 9800 + i, i + 1, l.GoodsReceiptLineId, open.GrNo, open.GrSeq, open.ItemId,
                                open.ItemCd, open.ItemName, open.Unit, open.GrQty, open.GrPrc, l.Qty, l.Prc ?? open.GrPrc, l.TaxCodeId ?? 1,
                                "VAT10", l.Qty * (l.Prc ?? open.GrPrc), 0m, l.Deductible ?? true, 0m);
                        })],
                    });
                case "/api/mm/supplier-invoices/801":
                    return Json(Invoice);
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        private static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, null, "application/problem+json") };
    }
}

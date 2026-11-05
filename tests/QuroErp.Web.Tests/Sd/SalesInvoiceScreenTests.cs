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
using QuroErp.Contracts.Sd.SalesInvoices;
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
using QuroErp.Web.Sd.SalesInvoices;
using QuroErp.Web.Sd.SalesOrders;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Sd;

/// <summary>Sales invoice screen /sd/sales-invoice (SD-UC-003, TASK-SD-006) against a fake API. The company is US (base USD).</summary>
public class SalesInvoiceScreenTests : BunitContext
{
    private const string Screen = "sales-invoice";
    private readonly FakeSalesInvoiceApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public SalesInvoiceScreenTests()
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
        Services.AddHttpClient<SalesInvoiceApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
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

    [Fact, Trait("Id", "SD-UC-003-T01"), Trait("Rule", "MM-BR-703")]
    public void New_StartsFromTheUsersWarehouse_WithImportsAndVoidDisabled_UntilACustomerIsPicked()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=sales-invoice-doc-no]").TextContent.Should().Be("새 문서");
        page.Field("sales-invoice-header-warehouse-code").Selected.Should().BeEquivalentTo("MAIN");
        page.Field("sales-invoice-header-warehouse-code").Options.Should().ContainSingle("never the consignment warehouse")
            .Which.Code.Should().BeEquivalentTo("MAIN");
        page.Field("sales-invoice-header-currency-code").Value.Should().BeEquivalentTo("USD");
        Frame(page).Header.DueDt.Should().Be(Frame(page).Header.DnDt);
        Button(page, "import-shipment").HasAttribute("disabled").Should().BeTrue();
        Button(page, "import-order").HasAttribute("disabled").Should().BeTrue();
        Button(page, "void").HasAttribute("disabled").Should().BeTrue("a blocked command is disabled, not hidden");
        Button(page, "tax-invoice").HasAttribute("disabled").Should().BeTrue();
        page.Find(".quro-entry-lines-head").TextContent.Should().Contain("거래처를 먼저 선택하세요.");
    }

    [Fact, Trait("Id", "SD-UC-003-T01"), Trait("Rule", "SD-BR-118")]
    public void PickedCustomer_FillsTheBillToDiscountStatusRepAndShipTo()
    {
        var page = RenderAs("admin");

        PickCustomer(page);

        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        var header = Frame(page).Header;
        (header.BillTo, header.DcStatus, header.Rep, header.Terms, header.ShipName, header.ShipZipCode)
            .Should().Be(("1 Main St, Springfield, CA, 90001, USA", "A", "KIM", "NET_30", "ACME DOCK", "90001"));
        page.Field("sales-invoice-header-bill-to").IsReadOnly.Should().BeTrue();
        Button(page, "import-shipment").HasAttribute("disabled").Should().BeFalse();
        Button(page, "import-order").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact, Trait("Id", "SD-UC-003-T02"), Trait("Rule", "SD-BR-115"), Trait("Rule", "SD-BR-406")]
    public void ImportShipment_AddsTheUnbilledLines_AndSavePostsThemWithoutPrice()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));

        Button(page, "import-shipment").Click();
        page.WaitForAssertion(() => page.LookupRows("sales-invoice-shipment-lookup").Should().HaveCount(1, "the lines are grouped by shipment"));
        page.PickLookupRow<InvoiceShipmentChoice, SalesInvoice>("sales-invoice-shipment-lookup", 0);

        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(2));
        var first = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (first.ItemCd, first.DnQty, first.DnPrc, first.WarehouseCode, first.ShipmentLineId, first.Source, first.DnAmt)
            .Should().Be(("BOLT", 3m, 10m, "MAIN", 6001L, "SH0000001-1", 30m));
        Frame(page).Header.BillingFlow.Should().Be("SHIP_THEN_BILL");
        Button(page, "import-order").HasAttribute("disabled").Should().BeTrue("shipment lines and bill-first lines are never mixed");

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        var request = _api.Saved[0];
        (request.CustomerId, request.WarehouseId, request.CurrencyCode, request.ExchangeRate, request.Rep, request.UpdatedAt)
            .Should().Be((801L, (long?)1L, "USD", (decimal?)1m, "KIM", (DateTimeOffset?)null));
        request.Lines.Select(l => (l.Id, l.ItemId, l.DnQty, l.DnPrc, l.WarehouseId, l.ShipmentLineId, l.SalesOrderLineId))
            .Should().Equal(
                ((long?)null, 11L, 3m, (decimal?)null, (long?)null, (long?)6001L, (long?)null),
                ((long?)null, 12L, 1m, (decimal?)null, (long?)null, (long?)6002L, (long?)null));
        page.WaitForAssertion(() => page.Find("[data-testid=sales-invoice-doc-no]").TextContent.Should().Be("DN0000001"));
    }

    [Fact, Trait("Id", "SD-UC-003-T03"), Trait("Rule", "SD-BR-141")]
    public void ImportOrder_AddsTheBillFirstLinesFromTheHeaderWarehouse_AndTakesTheOrderHeader()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));

        Button(page, "import-order").Click();
        page.WaitForAssertion(() => page.LookupRows("sales-invoice-order-lookup").Should().HaveCount(1));
        page.PickLookupRow<InvoiceOrderChoice, SalesInvoice>("sales-invoice-order-lookup", 0);

        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        var line = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (line.ItemCd, line.DnQty, line.DnPrc, line.WarehouseCode, line.WarehouseId, line.SalesOrderLineId, line.Source)
            .Should().Be(("PANEL", 4m, 25m, "MAIN", 1L, 5001L, "SO2611000002-1"));
        var header = Frame(page).Header;
        (header.BillingFlow, header.SoNo, header.ShipVia, header.ShipName).Should().Be(("BILL_THEN_SHIP", "SO2611000002", "UPS", "ACME YARD"));
        Button(page, "import-shipment").HasAttribute("disabled").Should().BeTrue("bill-first lines and shipment lines are never mixed");
        var request = line.ToRequest();
        (request.DnPrc, request.WarehouseId, request.SalesOrderLineId).Should().Be(((decimal?)null, (long?)1L, (long?)5001L));
    }

    [Fact, Trait("Id", "SD-UC-003-T04"), Trait("Rule", "SD-BR-116"), Trait("Rule", "SD-BR-401")]
    public async Task DirectLine_IsAServiceItem_ThatTakesTheLineDefaults_AndSendsItsPrice()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        var frame = Frame(page);

        var items = await page.InvokeAsync(() => frame.Lookup!(new LookupQuery("sdSoItem", null)));
        items.Select(r => r["itemCd"]).Should().Equal(["FREIGHT"], "a direct line bills an item without stock");

        Button(page, "add-row").Click();
        (await Lines(page).EditAsync(0, "itemCd", "freight")).Should().BeTrue();

        var key = Lines(page).KeyOf(0);
        page.WaitForAssertion(() => frame.Lines.GetItem(key).DnPrc.Should().Be(22m));
        var line = frame.Lines.GetItem(key);
        (line.ItemId, line.DnQty, line.TaxCode, line.WarehouseId, line.DnAmt).Should().Be((13L, 1m, "ST10", 1L, 22m));
        var request = line.ToRequest();
        (request.DnPrc, request.TaxCodeId, request.ShipmentLineId, request.SalesOrderLineId)
            .Should().Be(((decimal?)22m, (long?)1L, (long?)null, (long?)null));
    }

    [Fact, Trait("Id", "SD-UT-107"), Trait("Rule", "SD-BR-107")]
    public async Task SavedLine_IsNotChanged_ButCanBeDeleted_AndTheSaveLeavesItOut()
    {
        _api.Invoice = Invoice();
        var page = Open();
        var lines = Lines(page);

        (await lines.EditAsync(0, "dnQty", "1")).Should().BeFalse("a saved line is deleted and added again, not changed");
        Frame(page).Lines.MoveTo(lines.KeyOf(0));
        page.Render();
        Button(page, "mark-delete").Click();
        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        (_api.Saved[0].Lines.Count, _api.Saved[0].UpdatedAt, _api.Saved[0].ExchangeRate).Should().Be((0, (DateTimeOffset?)AuditTimes.Updated, (decimal?)null));
    }

    [Theory, Trait("Id", "SD-UT-105"), Trait("Rule", "SD-BR-105"), Trait("Rule", "SD-BR-104"), Trait("Rule", "SD-BR-113")]
    // An open invoice of shipment lines takes more shipment lines, never bill-first order lines (SD-BR-141)
    [InlineData("OP", "brand", "save,add-row,mark-delete,import-shipment,void,delete,pdf")]
    [InlineData("PA", "warn", "pdf")]
    [InlineData("CL", "ok", "pdf")]
    [InlineData("VO", "neutral", "pdf")]
    public void Status_SetsTheChipAndTheEnabledButtons(string status, string chip, string enabled)
    {
        _api.Invoice = Invoice(status);

        var page = Open();
        Frame(page).Lines.MoveTo(Lines(page).KeyOf(0));
        page.Render();

        page.Find("[data-testid=sales-invoice-status-chip] .quro-chip").ClassList.Should().Contain($"quro-chip-{chip}");
        Enabled(page).Except(["search", "new"]).Should().BeEquivalentTo(enabled.Split(','));
    }

    [Fact, Trait("Id", "SD-UT-136"), Trait("Rule", "SD-BR-136")]
    public void InvoiceMadeByAShipment_HasNoImports_NoVoid_AndItsShipmentLinesStay()
    {
        _api.Invoice = Invoice(billingFlow: "SHIP_AND_BILL");
        var page = Open();
        Frame(page).Lines.MoveTo(Lines(page).KeyOf(0));
        page.Render();

        Enabled(page).Except(["search", "new"]).Should().BeEquivalentTo("save", "add-row", "pdf");
        page.Field("sales-invoice-header-source-document").Value.Should().Be("SH0000001");
    }

    [Fact, Trait("Id", "SD-UT-144"), Trait("Rule", "SD-BR-144")]
    public void BillFirstLineWithShippedQty_CannotBeMarkedForDelete_AndBlocksVoid()
    {
        _api.Invoice = BillFirstInvoice(shipped: 1m);
        var page = Open();
        Frame(page).Lines.MoveTo(Lines(page).KeyOf(0));
        page.Render();

        Button(page, "mark-delete").HasAttribute("disabled").Should().BeTrue();
        Button(page, "void").HasAttribute("disabled").Should().BeTrue();
        Button(page, "cancel-unshipped").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact, Trait("Id", "SD-UC-003-T05"), Trait("Rule", "SD-BR-113")]
    public void Void_AsksFirst_ThenShowsTheVoidInvoice()
    {
        _api.Invoice = Invoice();
        var page = Open();
        _confirm.Answer = true;

        Button(page, "void").Click();

        page.WaitForAssertion(() => Frame(page).Header.Status.Should().Be("VO"));
        _confirm.Questions.Should().ContainSingle().Which.Should().Contain("Void할까요");
        _api.Voided.Should().Equal(AuditTimes.Updated);
    }

    [Fact, Trait("Id", "SD-UC-003-T06"), Trait("Rule", "SD-BR-145"), Trait("Rule", "SD-BR-208")]
    public void CancelUnshipped_SendsTheQtyDateAndReasons_AndShowsTheCreditMemo()
    {
        _api.Invoice = BillFirstInvoice();
        var page = Open();

        Button(page, "cancel-unshipped").Click();
        page.WaitForElement("[data-testid=sales-invoice-cancel-dialog]");
        page.FindAll("[data-testid=sales-invoice-cancel-dialog-qty]").Should().HaveCount(1);
        page.Find("[data-testid=sales-invoice-cancel-dialog-qty]").Change("2");
        page.Find("[data-testid=sales-invoice-cancel-dialog-reason-04]").Change(true);
        page.Find("[data-testid=sales-invoice-cancel-dialog-ok]").Click();

        page.WaitForAssertion(() => _api.Cancelled.Should().ContainSingle());
        var request = _api.Cancelled[0];
        request.Lines.Should().Equal(new CancelUnshippedLineRequest(9001, 2m));
        (request.CancelDt, request.UpdatedAt).Should().Be((DateOnly.FromDateTime(DateTime.Today), AuditTimes.Updated));
        request.Reasons.Should().Equal(false, false, false, true, false, false, false, false);
        page.WaitForAssertion(() => page.Find("[data-testid=sales-invoice-message]").TextContent.Should().Contain("CM0000001"));
        page.FindAll("[data-testid=sales-invoice-cancel-dialog]").Should().BeEmpty();
        page.FindAll("[data-testid=sales-invoice-cancellations] tbody tr").Should().HaveCount(1);
    }

    [Fact, Trait("Id", "SD-UT-145"), Trait("Rule", "SD-BR-145")]
    public void CancelUnshipped_AboveTheUnshippedQty_StaysInTheDialog()
    {
        _api.Invoice = BillFirstInvoice();
        var page = Open();

        Button(page, "cancel-unshipped").Click();
        page.Find("[data-testid=sales-invoice-cancel-dialog-qty]").Change("5");
        page.Find("[data-testid=sales-invoice-cancel-dialog-ok]").Click();

        page.Find("[data-testid=sales-invoice-cancel-dialog-message]").TextContent.Should().Contain("미출하 수량 이하");
        _api.Cancelled.Should().BeEmpty();
    }

    [Theory, Trait("Id", "SD-UT-146"), Trait("Rule", "SD-BR-146")]
    [InlineData(false)]
    [InlineData(true)]
    public void UndoCancellation_AsksFirst_AndIsBlockedOnceTheCreditMemoIsApplied(bool applied)
    {
        _api.Invoice = BillFirstInvoice(cancelled: 1m, applied: applied);
        var page = Open();
        var undo = page.Find("[data-testid=sales-invoice-cancellations-undo]");

        undo.HasAttribute("disabled").Should().Be(applied);
        if (applied)
        {
            return;
        }
        _confirm.Answer = true;
        undo.Click();

        page.WaitForAssertion(() => _api.Undone.Should().Equal(7701L));
        _confirm.Questions.Should().ContainSingle().Which.Should().Contain("CM0000001");
    }

    [Fact, Trait("Id", "SD-UT-309"), Trait("Rule", "SD-BR-309")]
    public void CreditLimitWarning_Yes_SavesAgainWithTheAnswer()
    {
        _api.Invoice = Invoice();
        var page = Open();
        page.Field("sales-invoice-header-remarks").Change("Big invoice");
        _api.SaveProblems.Enqueue("""{"status":422,"code":"SD-W-309","args":["1000.00","1200.00"],"confirmField":"confirmCreditLimit"}""");
        _confirm.Answer = true;

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().HaveCount(2));
        (_api.Saved[0].ConfirmCreditLimit, _api.Saved[1].ConfirmCreditLimit).Should().Be(((bool?)null, (bool?)true));
    }

    [Fact, Trait("Id", "SD-UT-309"), Trait("Rule", "SD-BR-309")]
    public void CreditLimitWarning_No_DoesNotSave()
    {
        _api.Invoice = Invoice();
        var page = Open();
        page.Field("sales-invoice-header-remarks").Change("Big invoice");
        _api.SaveProblems.Enqueue("""{"status":422,"code":"SD-W-309","args":["1000.00","1200.00"],"confirmField":"confirmCreditLimit"}""");
        _confirm.Answer = false;

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-invoice-message]").GetAttribute("data-level").Should().Be("error"));
        _api.Saved.Should().ContainSingle("no answer means no second save");
    }

    [Theory, Trait("Id", "SD-UT-117"), Trait("Rule", "SD-BR-117")]
    [InlineData(true)]
    [InlineData(false)]
    public void ShipToQuestion_SavesAgainWithTheAnswer_EvenWhenItIsNo(bool answer)
    {
        _api.Invoice = Invoice();
        var page = Open();
        page.Field("sales-invoice-header-remarks").Change("New dock");
        _api.SaveProblems.Enqueue("""{"status":422,"code":"SD-W-009","confirmField":"updateCustomerShipTo"}""");
        _confirm.Answer = answer;

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().HaveCount(2));
        (_api.Saved[0].UpdateCustomerShipTo, _api.Saved[1].UpdateCustomerShipTo).Should().Be(((bool?)null, (bool?)answer));
        _confirm.Questions.Should().ContainSingle();
    }

    [Fact, Trait("Id", "SD-UC-003-T04")]
    public void SaveAfterSomeoneElseSaved_Shows409Message()
    {
        _api.Invoice = Invoice();
        var page = Open();
        page.Field("sales-invoice-header-remarks").Change("Late");
        _api.SaveProblems.Enqueue("""{"status":409,"code":"Common-E-004"}""");
        _api.SaveProblemStatus = HttpStatusCode.Conflict;

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-invoice-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "SD-UC-003-T01"), Trait("Rule", "SD-BR-131")]
    public void ForeignInvoice_ShowsTheTotalInTheBaseCurrency()
    {
        _api.Invoice = Invoice() with { CurrencyCode = "EUR", ExchangeRate = 1.1m };

        var page = Open();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-invoice-totals-base-total]").TextContent.Should().Contain("48.40").And.Contain("USD"));
        page.Find("[data-testid=sales-invoice-totals-total]").TextContent.Should().Contain("44.00").And.Contain("EUR");
    }

    [Fact, Trait("Id", "SD-UC-003-T01"), Trait("Rule", "SD-BR-131")]
    public void BaseCurrencyInvoice_ShowsNoBaseTotal()
    {
        _api.Invoice = Invoice();

        var page = Open();

        page.Find("[data-testid=sales-invoice-totals-total]").TextContent.Should().Contain("44.00").And.Contain("USD");
        page.FindAll("[data-testid=sales-invoice-totals-base-total]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UC-003-T01")]
    public void Print_DownloadsTheInvoice()
    {
        _api.Invoice = Invoice();
        var page = Open();

        Button(page, "pdf").Click();

        page.WaitForAssertion(() => JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("quroDownload"));
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/sd/sales-invoices/901/print/sales-invoice.pdf");
    }

    [Fact, Trait("Id", "SD-UT-134"), Trait("Rule", "SD-BR-134")]
    public void TaxInvoice_IsForAKoreanCompany()
    {
        _api.Country = "KR";
        _api.Invoice = Invoice();
        var page = Open();

        Button(page, "tax-invoice").HasAttribute("disabled").Should().BeFalse();
        Button(page, "tax-invoice").Click();

        page.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/sd/sales-invoices/901/print/tax-invoice.pdf"));
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Rule", "BC-BR-214")]
    public void UserWithoutFullAccess_CanOnlyFindAndPrint()
    {
        _api.Invoice = BillFirstInvoice();
        var page = Open("user");

        Enabled(page).Except(["search", "new"]).Should().BeEquivalentTo("pdf");
        page.Find("[data-testid=sales-invoice-header-customer-lookup]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UC-003-T01")]
    public void Labels_AreTheEnglishResources_InEnglish()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");
        _api.Invoice = BillFirstInvoice();

        var page = Open();

        page.Find("[data-testid=sales-invoice-title] h1").TextContent.Should().Be("Sales Invoice");
        Button(page, "import-shipment").TextContent.Should().Be("Import Shipment");
        Button(page, "cancel-unshipped").TextContent.Should().Be("Cancel Unshipped");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["Item Code", "Warehouse", "Lot", "Qty", "Shipped Qty", "Unshipped Qty"]);
        page.Find("[data-testid=sales-invoice-status-chip]").TextContent.Should().Contain("Open");
    }

    private IRenderedComponent<SalesInvoice> RenderAs(string role, string? dnNo = null)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<SalesInvoice>(p => p.Add(x => x.DnNo, dnNo));
        page.WaitForElement("[data-testid=sales-invoice-header-customer-name]");
        return page;
    }

    private IRenderedComponent<SalesInvoice> Open(string role = "admin")
    {
        var page = RenderAs(role, "DN0000001");
        page.WaitForAssertion(() => page.Find("[data-testid=sales-invoice-doc-no]").TextContent.Should().Be("DN0000001"));
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(1));
        return page;
    }

    private static void PickCustomer(IRenderedComponent<SalesInvoice> page)
    {
        page.Find("[data-testid=sales-invoice-header-customer-lookup]").Click();
        page.WaitForAssertion(() => page.LookupRows("sales-invoice-customer-lookup").Should().NotBeEmpty());
        page.PickLookupRow<CustomerDto, SalesInvoice>("sales-invoice-customer-lookup", 0);
    }

    private static DataGridPage<SalesInvoiceLine> Lines(IRenderedComponent<SalesInvoice> page) =>
        page.DataGrid<SalesInvoiceLine, SalesInvoice>("sales-invoice-line");

    private static QuroHeaderDetail<SalesInvoiceHeader, SalesInvoiceLine> Frame(IRenderedComponent<SalesInvoice> page) =>
        page.FindComponent<QuroHeaderDetail<SalesInvoiceHeader, SalesInvoiceLine>>().Instance;

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<SalesInvoice> page, string action) =>
        page.Find($"[data-testid={Screen}-toolbar-{action}]");

    private static IReadOnlyList<string> Enabled(IRenderedComponent<SalesInvoice> page) =>
        [.. page.FindAll("[role=toolbar] button:not([disabled])").Select(b => b.GetAttribute("data-testid")!.Replace($"{Screen}-toolbar-", ""))];

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>A SHIP_THEN_BILL invoice of one shipment line (or the invoice a SHIP_AND_BILL shipment made).</summary>
    private static SalesInvoiceDto Invoice(string status = "OP", string billingFlow = "SHIP_THEN_BILL") => new(
        Id: 901, DnNo: "DN0000001", Status: status, BillingFlow: billingFlow, CustomerId: 801, CustomerName: "ACME", SalesOrderId: null,
        SoNo: null, DnDt: new DateOnly(2026, 11, 5), DueDt: new DateOnly(2026, 12, 5), WarehouseId: 1, CurrencyCode: "USD", ExchangeRate: 1m,
        Rep: "KIM", Terms: "NET_30", ShipVia: "UPS", FreightCharge: "N", ShipName: "ACME DOCK", ShipAddr1: "1 Main St", ShipAddr2: null,
        ShipCity: "Springfield", ShipState: "CA", ShipZipCode: "90001", ShipCountry: "USA", ShipPhone: null, ShipFax: null, Remarks: null,
        TaxExemptionCertNo: null, SupplyAmt: 40m, TaxAmt: 4m, TotDnAmt: 44m, TotBaseAmt: 44m, BalAmt: status == "PA" ? 20m : 44m,
        PaymentApplied: status == "PA" ? 24m : 0m, CreditApplied: 0m, AvailableCredit: 0m,
        ShipmentId: billingFlow == "SHIP_AND_BILL" ? 701 : null, ShipNo: billingFlow == "SHIP_AND_BILL" ? "SH0000001" : null,
        Lines: [new SalesInvoiceLineDto(9001, 1, 11, "BOLT", "Hex Bolt", "EA", 1, "MAIN", null, null, 4m, 10m, 40m, null, 0m, 12m, 1, 40m, 4m,
            null, null, null, 6001, "SH0000001", 1, 0m, 0m, 0m)],
        Cancellations: [], UpdatedAt: AuditTimes.Updated, CreatedAt: AuditTimes.Created, CreatedBy: "setup", UpdatedBy: "kim");

    /// <summary>A BILL_THEN_SHIP invoice of one order line of 4, with what was shipped and cancelled of it.</summary>
    private static SalesInvoiceDto BillFirstInvoice(decimal shipped = 0m, decimal cancelled = 0m, bool applied = false) => Invoice() with
    {
        BillingFlow = "BILL_THEN_SHIP",
        SalesOrderId = 602,
        SoNo = "SO2611000002",
        Lines = [new SalesInvoiceLineDto(9001, 1, 12, "PANEL", "Panel", "EA", 1, "MAIN", null, null, 4m, 25m, 100m, null, 0m, 25m, 1, 100m, 10m,
            5001, "SO2611000002", 1, null, null, null, shipped, cancelled, 4m - shipped - cancelled)],
        Cancellations = cancelled == 0m
            ? []
            : [new SalesInvoiceCancellationDto(7701, "CM0000001", new DateOnly(2026, 11, 6), -27.5m, applied ? 0m : -27.5m, applied)],
    };

    /// <summary>Sales invoice, sales order defaults, customer, item, rate, localization, tax code, warehouse, profile, lot and code API.</summary>
    private sealed class FakeSalesInvoiceApi : HttpMessageHandler
    {
        private static readonly CustomerDto Acme = new(
            Id: 801, CName: "ACME", ShipName: "ACME DOCK", Contact: "Kim", Contact2: null, Phone1: "555-0100", Phone2: null, Phone3: null,
            FaxNo: null, Email: null, Rep: "KIM", PriceLevel: "ACME", FreightCharge: "N", BalAmt: 70.5m, Remarks: null, Memo2: null,
            Memo3: null, Terms: "NET_30", ValidFlag: "Y", Addr1: "1 Main St", Addr2: null, City: "Springfield", State: "CA",
            Country: "USA", ZipCode: "90001", ShipAddr1: "1 Main St", ShipAddr2: null, ShipCity: "Springfield", ShipState: "CA",
            ShipCountry: "USA", ShipZipCode: "90001", ShipPhone: null, ShipFax: null, CreditLimit: 1000m, DcStatus: "A",
            StartDt: new DateOnly(2026, 1, 5), BillingFlow: "SHIP_THEN_BILL", CurrencyCode: null, TaxCodeId: 1,
            TaxCode: "ST10", TaxPartyType: null, BusinessRegNo: null, CeoName: null, BusinessType: null, BusinessItem: null,
            CreatedAt: AuditTimes.Created, CreatedBy: "setup", UpdatedAt: AuditTimes.Updated, UpdatedBy: "kim");

        private static readonly ItemDto[] Items =
        [
            Item(11, "BOLT", "Hex Bolt", "INVENTORY", 12m),
            Item(12, "PANEL", "Panel", "INVENTORY", 25m),
            Item(13, "FREIGHT", "Freight", "SERVICE", 30m),
        ];

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<SaveSalesInvoiceRequest> Saved { get; } = [];

        public List<DateTimeOffset> Voided { get; } = [];

        public List<CancelUnshippedRequest> Cancelled { get; } = [];

        public List<long> Undone { get; } = [];

        public SalesInvoiceDto? Invoice { get; set; }

        public string Country { get; set; } = "US";

        /// <summary>The bodies of the next saves' errors, in order; a save without one passes.</summary>
        public Queue<string> SaveProblems { get; } = new();

        public HttpStatusCode SaveProblemStatus { get; set; } = HttpStatusCode.UnprocessableEntity;

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
                    return Json(new LocalizationDto(Country, "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "LINE", [],
                        [new CurrencyDto("USD", "US Dollar", 2, "US"), new CurrencyDto("EUR", "Euro", 2, "DE")],
                        [new CountryDecimalDto(Country, "SD", 2)],
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
                    return Json(new List<LotBalanceDto> { new(1, "L2611A", 30m) });
                case "/api/sd/customers":
                    return Json(new CustomerListDto([Acme], 1));
                case "/api/sd/customers/801":
                    return Json(Acme);
                case "/" + ItemRoutes.Base:
                    var code = query["itemCd"];
                    var name = query["itemName"];
                    return Json(new ItemListDto([.. Items.Where(i =>
                        (string.IsNullOrEmpty(code) || i.ItemCd.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                        && (string.IsNullOrEmpty(name) || (i.ItemName ?? "").Contains(name, StringComparison.OrdinalIgnoreCase)))], Items.Length));
                case "/api/sd/sales-orders/line-defaults":
                    var item = Items.First(i => i.ItemCd.Equals(query["itemCd"], StringComparison.OrdinalIgnoreCase));
                    return Json(new SalesOrderLineDefaultsDto(item.Id, item.ItemCd, item.ItemName, item.Unit, 1m, 22m, item.SoUsdPrc, null, 0m, 1, null));
                case "/api/sd/sales-orders/discount-rates":
                    return Json(new List<SalesOrderDiscountRateDto> { new("D10", 10m) });
                case "/api/sd/shipments/unbilled-lines":
                    return Json(new List<UnbilledShipmentLineDto>
                    {
                        new(701, "SH0000001", new DateOnly(2026, 11, 4), 6001, 1, 11, "BOLT", "Hex Bolt", 1, 5m, 2m, 3m, 10m, null, 0m, 1, 601, "SO2611000001"),
                        new(701, "SH0000001", new DateOnly(2026, 11, 4), 6002, 2, 12, "PANEL", "Panel", 1, 1m, 0m, 1m, 25m, null, 0m, 1, 601, "SO2611000001"),
                    });
                case "/api/sd/sales-invoices/open-order-lines":
                    return Json(new List<OpenBillFirstOrderLineDto>
                    {
                        new(602, "SO2611000002", new DateOnly(2026, 11, 4), "USD", 1m, "KIM", "NET_30", null, "UPS", "N", "ACME YARD", "9 Yard Rd",
                            null, "Springfield", "CA", "90002", "USA", null, 5001, 1, 12, "PANEL", "Panel", "EA", 4m, 0m, 4m, 25m, null, 0m, 1, 40m),
                    });
                case "/api/sd/sales-invoices" when request.Method == HttpMethod.Get:
                    return Json(Invoice is null
                        ? new List<SalesInvoiceSummaryDto>()
                        : [new(Invoice.Id, Invoice.DnNo, Invoice.Status, Invoice.BillingFlow, Invoice.DnDt, Invoice.CustomerId,
                            Invoice.CustomerName, Invoice.CurrencyCode, Invoice.TotDnAmt, Invoice.BalAmt)]);
                case "/api/sd/sales-invoices" when request.Method == HttpMethod.Post:
                case "/api/sd/sales-invoices/901" when request.Method == HttpMethod.Put:
                    Saved.Add((await request.Content!.ReadFromJsonAsync<SaveSalesInvoiceRequest>(cancellationToken))!);
                    if (SaveProblems.TryDequeue(out var problem))
                    {
                        return new HttpResponseMessage(SaveProblemStatus)
                        {
                            Content = new StringContent(problem, null, "application/problem+json"),
                        };
                    }
                    var saved = Saved[^1];
                    return Json(SalesInvoiceScreenTests.Invoice() with
                    {
                        Lines = [.. saved.Lines.Select((l, i) => new SalesInvoiceLineDto(
                            l.Id ?? 9100 + i, i + 1, l.ItemId, Items.First(x => x.Id == l.ItemId).ItemCd, l.ItemName, "EA", l.WarehouseId ?? 1, "MAIN",
                            null, l.LotNo, l.DnQty, l.DnPrc ?? 10m, l.DnQty * (l.DnPrc ?? 10m), null, 0m, null, l.TaxCodeId ?? 1, 0m, 0m,
                            l.SalesOrderLineId, null, null, l.ShipmentLineId, null, null, 0m, 0m, 0m))],
                    });
                case "/api/sd/sales-invoices/901/void":
                    Voided.Add((await request.Content!.ReadFromJsonAsync<VoidSalesInvoiceRequest>(cancellationToken))!.UpdatedAt);
                    return Json(Invoice! with { Status = "VO" });
                case "/api/sd/sales-invoices/901/cancel-unshipped":
                    var cancel = (await request.Content!.ReadFromJsonAsync<CancelUnshippedRequest>(cancellationToken))!;
                    Cancelled.Add(cancel);
                    Invoice = BillFirstInvoice(cancelled: cancel.Lines.Sum(l => l.Qty));
                    return Json(new CancelUnshippedResult(7701, "CM0000001"));
                case "/api/sd/sales-invoices/901/cancellations/7701" when request.Method == HttpMethod.Delete:
                    Undone.Add(7701);
                    Invoice = BillFirstInvoice();
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/api/sd/sales-invoices/901":
                    return Json(Invoice);
                case "/api/sd/sales-invoices/901/print/sales-invoice.pdf":
                case "/api/sd/sales-invoices/901/print/tax-invoice.pdf":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF-1.7"u8.ToArray()) };
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static IReadOnlyList<CodeItemDto> Codes(string major) => major switch
        {
            "STATE" => [new("CA", "California"), new("NY", "New York")],
            "SHIPMENT_VIA" => [new("UPS", "UPS")],
            "SALES_REP" => [new("KIM", "Kim")],
            "TERMS" => [new("NET_30", "Net 30")],
            "DISCOUNT_STATUS" => [new("A", "Active")],
            _ => [],
        };

        private static ItemDto Item(long id, string code, string name, string type, decimal salesPrice) => new(
            id, code, name, null, null, type, "EA", null, "Y", 0m, salesPrice, null, null, 0m, null, null, null, null, null, null, true,
            "TAXABLE", "", 0m, 0m, 0m, AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

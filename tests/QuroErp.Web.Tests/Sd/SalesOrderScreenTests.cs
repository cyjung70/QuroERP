using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Quro.Grid.Data;
using QuroErp.Contracts.Bc.FieldPolicies;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Bc.Tax;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Contracts.Sd.Customers;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Codes;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.Tax;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Sd;
using QuroErp.Web.Components.Pages.Sd.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Sd.Customers;
using QuroErp.Web.Sd.SalesOrders;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Sd;

/// <summary>Sales order screen /sd/sales-order (SD-UC-002, TASK-SD-004) against a fake API. The company is US (base USD).</summary>
public class SalesOrderScreenTests : BunitContext
{
    private const string Screen = "sales-order";
    private readonly FakeSalesOrderApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public SalesOrderScreenTests()
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
        Services.AddHttpClient<SalesOrderApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CustomerApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ExchangeRateApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<TaxCodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<FieldPolicyApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<FieldPolicyCache>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "SD-UC-002-T01"), Trait("Rule", "SD-BR-021")]
    public void New_StartsInTheBaseCurrency_WithTheFileCommandsDisabled_UntilACustomerIsPicked()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=sales-order-doc-no]").TextContent.Should().Be("새 문서");
        page.FindAll("[data-testid=sales-order-status-chip]").Should().BeEmpty();
        page.Field("sales-order-header-currency-code").Selected.Should().BeEquivalentTo("USD");
        page.Field("sales-order-header-exchange-rate").Value.Should().Be("1");
        Enabled(page).Should().BeEquivalentTo("search", "new", "save", "add-row");
        FileInput(page, "line-file").HasAttribute("disabled").Should().BeTrue();
        FileInput(page, "barcode-file").HasAttribute("disabled").Should().BeTrue();
        page.Find(".quro-entry-lines-head").TextContent.Should().Contain("거래처를 먼저 선택하세요.");
    }

    [Fact, Trait("Id", "SD-UT-008"), Trait("Rule", "SD-BR-008"), Trait("Rule", "SD-BR-031"), Trait("Rule", "SD-BR-035")]
    public void PickingACustomer_FillsTheShipToRepTermsAndBillingFlow_AndTheDueDateFromTheTerms()
    {
        var page = RenderAs("admin");
        var soDt = DateOnly.FromDateTime(DateTime.Today);

        PickCustomer(page);

        page.WaitForAssertion(() => page.Field("sales-order-header-customer-name").Value.Should().Be("ACME"));
        Frame(page).Header.Should().BeEquivalentTo(new
        {
            CustomerId = 801L,
            ShipName = "ACME DOCK",
            ShipZipCode = "90001",
            ShipState = "CA",
            Rep = "KIM",
            Terms = "NET_30",
            FreightCharge = "N",
            BillingFlow = "SHIP_THEN_BILL",
            DcStatus = "D10",
            CurrencyCode = "USD",
            DueDt = (DateOnly?)soDt.AddDays(30),
        });
        FileInput(page, "line-file").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact, Trait("Id", "SD-UC-002-T01"), Trait("Rule", "SD-BR-016"), Trait("Rule", "SD-BR-401"), Trait("Rule", "SD-BR-405")]
    public async Task NewLine_TakesTheLineDefaults_ShowsTheAmounts_AndSavePostsTheLines()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        var lines = Lines(page);

        Button(page, "add-row").Click();
        (await lines.EditAsync(0, "itemCd", "bolt")).Should().BeTrue();

        var key = lines.KeyOf(0);
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(key).SoPrc.Should().Be(9.5m));
        var line = Frame(page).Lines.GetItem(key);
        (line.ItemId, line.SoQty, line.DcCd, line.DcRate, line.StdPrc, line.TaxCode, line.SoAmt, line.TaxAmt)
            .Should().Be((11L, 1m, "D10", -10m, 12m, "ST10", 8.55m, 0.86m));
        page.Find("[data-testid=sales-order-totals-total]").TextContent.Should().Contain("9.41").And.Contain("USD");

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().ContainSingle());
        var request = _api.Saved[0];
        (request.CustomerId, request.Rep, request.ShipZipCode, request.UpdatedAt).Should().Be((801L, "KIM", "90001", (DateTimeOffset?)null));
        request.Lines.Select(l => (l.Id, l.ItemId, l.SoQty, l.SoPrc, l.DcCd, l.TaxCodeId)).Should().Equal(((long?)null, 11L, (decimal?)1m, (decimal?)9.5m, "D10", (long?)1));
        page.WaitForAssertion(() => page.Find("[data-testid=sales-order-doc-no]").TextContent.Should().Be("SO2611000001"));
    }

    [Fact, Trait("Id", "SD-UT-404"), Trait("Rule", "SD-BR-404")]
    public async Task ClearingTheDiscountCode_RemovesTheDiscount_AndSendsNoDiscount()
    {
        var page = Open();
        var lines = Lines(page);

        await lines.EditAsync(0, "dcCd", "");

        var line = Frame(page).Lines.GetItem(lines.KeyOf(0));
        (line.DcRate, line.SoAmt).Should().Be((0m, 1000m));
        line.ToRequest().DcCd.Should().Be("", "an empty code is no discount, not the customer's default");
    }

    [Theory, Trait("Id", "SD-UT-003"), Trait("Id", "SD-UC-002-T07"), Trait("Rule", "SD-BR-003"), Trait("Rule", "SD-BR-012")]
    [InlineData("SO", 0, "neutral", "save,add-row,mark-delete,delete,pdf,picking-list,set-component-list")]
    [InlineData("RE", 4, "warn", "pdf,picking-list,set-component-list")]
    [InlineData("CL", 10, "ok", "pdf,picking-list,set-component-list")]
    public void Status_SetsTheChipAndTheEnabledButtons(string status, int shipped, string chip, string enabled)
    {
        _api.Order = Order(status, shipped);

        var page = Open();

        page.Find("[data-testid=sales-order-status-chip] .quro-chip").ClassList.Should().Contain($"quro-chip-{chip}");
        Enabled(page).Except(["search", "new"]).Should().BeEquivalentTo(enabled.Split(','));
        FileInput(page, "line-file").HasAttribute("disabled").Should().Be(status != "SO");
    }

    [Fact, Trait("Id", "SD-UT-003"), Trait("Rule", "SD-BR-003"), Trait("Rule", "SD-BR-023"), Trait("Rule", "SD-BR-035")]
    public async Task InvoicedLine_IsLocked_TheCustomerAndBillingFlowAreLocked()
    {
        _api.Order = Order("SO", 0, invoiced: 2, billingFlowLocked: true);

        var page = Open();

        (await Lines(page).EditAsync(0, "soQty", "5")).Should().BeFalse("an invoiced line takes no edits");
        Frame(page).Lines.GetItem(Lines(page).KeyOf(0)).SoQty.Should().Be(10m);
        page.Field("sales-order-header-customer-name").IsReadOnly.Should().BeTrue();
        page.Field("sales-order-header-billing-flow").IsReadOnly.Should().BeTrue();
        Button(page, "delete").HasAttribute("disabled").Should().BeTrue("an order with an invoice cannot be deleted");
    }

    [Theory, Trait("Id", "SD-UT-009"), Trait("Rule", "SD-BR-009"), Trait("Rule", "SD-BR-010")]
    [InlineData("SD-W-009", "updateCustomerShipTo", true)]
    [InlineData("SD-W-010", "updatePriceLevel", false)]
    public void SaveQuestion_AsksAndSendsTheAnswer_AndEitherAnswerSaves(string code, string field, bool answer)
    {
        var page = Open();
        page.Field("sales-order-header-remarks").Change("Changed");
        _api.SaveProblems.Enqueue($$"""{"status":422,"code":"{{code}}","args":["BOLT"],"confirmField":"{{field}}"}""");
        _confirm.Answer = answer;

        Button(page, "save").Click();

        page.WaitForAssertion(() => _api.Saved.Should().HaveCount(2));
        var second = _api.Saved[1];
        (field == "updateCustomerShipTo" ? second.UpdateCustomerShipTo : second.UpdatePriceLevel).Should().Be(answer);
        _confirm.Questions.Should().ContainSingle();
        page.WaitForAssertion(() => page.Find("[data-testid=sales-order-message]").TextContent.Should().Be("저장했습니다."));
    }

    [Fact, Trait("Id", "SD-UT-309"), Trait("Rule", "SD-BR-309")]
    public void CreditLimitWarning_No_DoesNotSave()
    {
        var page = Open();
        page.Field("sales-order-header-remarks").Change("Big order");
        _api.SaveProblems.Enqueue("""{"status":422,"code":"SD-W-309","args":["1000.00","1200.00"],"confirmField":"confirmCreditLimit"}""");
        _confirm.Answer = false;

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-order-message]").GetAttribute("data-level").Should().Be("error"));
        _api.Saved.Should().ContainSingle("no answer means no second save");
        Frame(page).IsDirty.Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UT-021"), Trait("Rule", "SD-BR-020"), Trait("Rule", "SD-BR-021")]
    public void LineFile_AddsTheLinesAfterTheLinesThere_WithoutSaving()
    {
        var page = Open();
        _api.FileResult = new SalesOrderLineFileResult(
            [new SalesOrderLineDefaultsDto(12, "PANEL", "Panel", "EA", 3m, 20m, 25m, null, 0m, 2, null)], []);

        page.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("PANEL,3", "lines.csv"));

        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(2));
        var added = Frame(page).Lines.GetItem(Lines(page).KeyOf(1));
        (added.ItemCd, added.ItemId, added.SoQty, added.SoPrc, added.SoAmt, added.TaxCode).Should().Be(("PANEL", 12L, 3m, 20m, 60m, "NONE"));
        _api.Saved.Should().BeEmpty();
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/sd/sales-orders/line-file"
                                            && r.RequestUri.Query.Contains("kind=ITEM", StringComparison.Ordinal)
                                            && r.RequestUri.Query.Contains("customerId=801", StringComparison.Ordinal));
        page.Find("[data-testid=sales-order-message]").TextContent.Should().Contain("1");
    }

    [Fact, Trait("Id", "SD-UT-019"), Trait("Rule", "SD-BR-019"), Trait("Rule", "SD-BR-022")]
    public void BarcodeFileWithErrors_AddsNothing_AndListsTheRows()
    {
        var page = Open();
        _api.FileResult = new SalesOrderLineFileResult([], [new SalesOrderLineFileErrorDto(2, "999,1", "NOT_FOUND")]);

        page.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText("200000000017,1\n999,1", "codes.csv"));

        page.WaitForAssertion(() => page.Find("[data-testid=sales-order-message]").TextContent.Should().Contain("999,1").And.Contain("품목 없음"));
        page.Find("[data-testid=sales-order-message]").GetAttribute("data-level").Should().Be("error");
        Lines(page).Rows.Should().HaveCount(1);
        _api.Requests.Should().Contain(r => r.RequestUri!.Query.Contains("kind=BARCODE", StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "SD-UT-033"), Trait("Rule", "SD-BR-033")]
    public void ShipStateChange_AsksToFillTheLineTaxCodesAgain()
    {
        var page = Open();
        _confirm.Answer = true;

        page.Field("sales-order-header-ship-state").Change("NY");

        page.WaitForAssertion(() => Frame(page).Lines.GetItem(Lines(page).KeyOf(0)).TaxCode.Should().Be("NONE"));
        _confirm.Questions.Should().ContainSingle().Which.Should().Contain("세금 코드");
        Frame(page).Lines.GetItem(Lines(page).KeyOf(0)).TaxAmt.Should().Be(0m);
    }

    [Theory, Trait("Id", "SD-UC-002-T01")]
    [InlineData("pdf", "/api/sd/sales-orders/601/print/sales-order.pdf")]
    [InlineData("picking-list", "/api/sd/sales-orders/601/print/picking-list.pdf")]
    [InlineData("set-component-list", "/api/sd/sales-orders/601/print/set-component-list.pdf")]
    public void PrintButtons_DownloadTheirPdf(string action, string path)
    {
        var page = Open();

        Button(page, action).Click();

        page.WaitForAssertion(() => JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("quroDownload"));
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == path);
    }

    [Fact, Trait("Id", "SD-UC-002-T04")]
    public void SaveAfterSomeoneElseSaved_Shows409Message()
    {
        var page = Open();
        page.Field("sales-order-header-remarks").Change("Late");
        _api.SaveProblems.Enqueue("""{"status":409,"code":"Common-E-004"}""");

        Button(page, "save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-order-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Rule", "BC-BR-214")]
    public void UserWithoutFullAccess_CanOnlyFindAndPrint()
    {
        var page = RenderAs("user");

        Button(page, "save").HasAttribute("disabled").Should().BeTrue();
        Button(page, "add-row").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=sales-order-header-customer-lookup]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UC-002-T01")]
    public void Labels_AreTheEnglishResources_InEnglish()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = Open();

        page.Find("[data-testid=sales-order-title] h1").TextContent.Should().Be("Sales Order");
        page.Find(".quro-entry-path").TextContent.Should().Be("SD > Sales");
        Button(page, "picking-list").TextContent.Should().Be("Picking list");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["Item code", "Unit price", "Discount code", "Discount %", "Billed qty"]);
        page.Find("[data-testid=sales-order-totals-subtotal]").TextContent.Should().Contain("Supply amount");
        page.Find("[data-testid=sales-order-totals-tax]").TextContent.Should().Contain("Tax (estimated)");
        page.Find("[data-testid=sales-order-totals-total]").TextContent.Should().Contain("990.00");
    }

    [Fact, Trait("Id", "SD-UC-002-T01")]
    public void Labels_AreTheKoreanResources_InKorean()
    {
        var page = Open();

        page.Find("[data-testid=sales-order-title] h1").TextContent.Should().Be("수주 등록");
        page.Find(".quro-entry-path").TextContent.Should().Be("SD > 판매");
        page.Find("[data-testid=sales-order-totals-subtotal]").TextContent.Should().Contain("공급가액");
        page.Find("[data-testid=sales-order-totals-tax]").TextContent.Should().Contain("세액(예상)");
        Lines(page).Headers.Select(h => h.Title).Should().Contain(["할인 코드", "할인율 %", "청구 수량"]);
    }

    [Fact, Trait("Id", "SD-UC-002-T01")]
    public void Header_FollowsTheOrderOfTheScreenItems_WithTheBaseTotalForAForeignDocument()
    {
        _api.Localization = KoreanCompany;
        _api.Order = Order() with { ExchangeRate = 1382.5m };

        var page = Open();

        page.WaitForAssertion(() => page.FindAll("[data-testid=sales-order-header-tot-base-amt]").Should().ContainSingle());
        var fields = page.FindAll("[data-testid^=sales-order-header-]").Select(e => e.GetAttribute("data-testid")!.Replace("sales-order-header-", ""));
        fields.Except(["customer-lookup", "ship-to-toggle"]).Should().Equal(
            "customer-name", "so-dt", "exp-ship-dt",
            "currency-code", "exchange-rate", "due-dt", "billing-flow",
            "tax-code", "rep", "terms", "dc-status",
            "ship-via", "freight-charge", "tot-base-amt",
            "ship-to", "ship-name", "ship-addr1", "ship-addr2", "ship-city", "ship-state", "ship-zip-code", "ship-country", "ship-phone", "ship-fax",
            "remarks");
    }

    [Fact, Trait("Id", "SD-UC-002-T01")]
    public void LineGrid_HasTheColumnsOfTheScreenItems_WithTheItemNameReadOnly()
    {
        var page = Open();

        Lines(page).Headers.Select(h => h.Field).Should().Equal(
            "seq", "itemCd", "itemName", "unit", "soQty", "soPrc", "dcCd", "dcRate", "soAmt", "taxCode", "taxAmt", "shipQty", "dnQty");
        Lines(page).IsReadOnly(0, "itemName").Should().BeTrue();
        Lines(page).IsReadOnly(0, "unit").Should().BeTrue();
    }

    [Fact, Trait("Id", "SD-UC-002-T01"), Trait("Rule", "SD-BR-009")]
    public void ShipTo_StartsCollapsed_TogglesOpen_AndStaysOpenWhileThePickedCustomerLeavesTheZipCodeEmpty()
    {
        var page = RenderAs("admin");
        ShipToBody(page).HasAttribute("hidden").Should().BeTrue();

        page.Find("[data-testid=sales-order-header-ship-to-toggle]").Click();
        ShipToBody(page).HasAttribute("hidden").Should().BeFalse();
        page.Find("[data-testid=sales-order-header-ship-to-toggle]").Click();
        ShipToBody(page).HasAttribute("hidden").Should().BeTrue();

        _api.Customer = _api.Customer with { ShipZipCode = null };
        PickCustomer(page);

        page.WaitForAssertion(() => ShipToBody(page).HasAttribute("hidden").Should().BeFalse());
    }

    [Fact, Trait("Id", "SD-UT-405"), Trait("Rule", "SD-BR-016"), Trait("Rule", "SD-BR-405"), Trait("Rule", "BC-BR-542")]
    public void AddRow_CopiesTheHeaderDefaults_AndNumbersTheLine()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => page.Field("sales-order-header-tax-code").Selected.Should().BeEquivalentTo("ST10"));

        Button(page, "add-row").Click();

        page.WaitForAssertion(() => Lines(page).Rows.Should().ContainSingle());
        var line = Frame(page).Lines.GetItem(Lines(page).KeyOf(0));
        (line.Seq, line.SoQty, line.TaxCode, line.TaxCodeId, line.DcCd, line.DcRate).Should().Be((1, 1m, "ST10", 1L, "D10", -10m));
    }

    [Fact, Trait("Id", "SD-UT-405"), Trait("Rule", "SD-BR-405"), Trait("Rule", "BC-BR-542")]
    public async Task PickingAnItem_KeepsTheHeaderDefaultsTheUserChanged()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        page.Field("sales-order-header-tax-code").Change("NONE");
        page.Field("sales-order-header-dc-status").Change("D5");
        Button(page, "add-row").Click();

        (await Lines(page).EditAsync(0, "itemCd", "bolt")).Should().BeTrue();

        var key = Lines(page).KeyOf(0);
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(key).SoPrc.Should().Be(9.5m));
        var line = Frame(page).Lines.GetItem(key);
        (line.TaxCode, line.TaxCodeId, line.DcCd, line.DcRate).Should().Be(("NONE", 2L, "D5", -5m));
    }

    [Fact, Trait("Id", "BC-UT-542"), Trait("Rule", "BC-BR-542"), Trait("Rule", "SD-BR-033")]
    public async Task PickingAnItem_TakesTheCodeTheApiChoseForIt_OverTheHeaderDefault()
    {
        var page = RenderAs("admin");
        PickCustomer(page);
        page.WaitForAssertion(() => Frame(page).Header.CustomerId.Should().Be(801));
        page.Field("sales-order-header-ship-state").Change("NY");
        Button(page, "add-row").Click();

        (await Lines(page).EditAsync(0, "itemCd", "bolt")).Should().BeTrue();

        var key = Lines(page).KeyOf(0);
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(key).TaxCode.Should().Be("NONE"));
        page.Field("sales-order-header-tax-code").Selected.Should().BeEquivalentTo("ST10");
    }

    [Fact, Trait("Id", "SD-UC-002-T01")]
    public void FindDialog_SendsTheNumberCustomerPeriodAndStatus_ToTheServer()
    {
        var page = RenderAs("admin");
        Button(page, "search").Click();
        page.WaitForAssertion(() => page.LookupRows("sales-order-find").Should().ContainSingle());

        page.Find("[data-testid=sales-order-find-keyword]").Change("so2611");
        page.Find("[data-testid=sales-order-find-from]").Change("2026-11-01");
        page.Find("[data-testid=sales-order-find-to]").Change("2026-11-30");
        page.Find("[data-testid=sales-order-find-status]").Change("RE");
        page.Find("[data-testid=sales-order-find-customer]").Change("nobody");
        page.Find("form.quro-search").Submit();

        page.WaitForAssertion(() => page.LookupRows("sales-order-find").Should().BeEmpty("no order has this customer"));
        var query = _api.Requests.Last(r => r.RequestUri!.AbsolutePath == "/api/sd/sales-orders").RequestUri!.Query;
        query.Should().Contain("soNo=so2611").And.Contain("customerName=nobody").And.Contain("status=RE").And.Contain("2026-11-01").And.Contain("2026-11-30");

        page.Find("[data-testid=sales-order-find-customer]").Change("acm");
        page.Find("form.quro-search").Submit();

        page.WaitForAssertion(() => page.LookupRows("sales-order-find").Should().ContainSingle());
    }

    [Theory, Trait("Id", "SD-UC-002-T05"), Trait("Rule", "SD-BR-005"), Trait("Rule", "SD-BR-007"), Trait("Rule", "SD-BR-034"), Trait("Rule", "BC-BR-413")]
    [InlineData("LINE")]
    [InlineData("DOCUMENT")]
    public void UsdOrderInAKoreanCompany_ShowsTheTotalsWithoutTheLineMarkedForDelete(string taxCalcMode)
    {
        _api.Localization = KoreanCompany with { TaxCalcMode = taxCalcMode };
        _api.Order = Order() with
        {
            ExchangeRate = 1382.5m,
            Lines =
            [
                OrderLine(9001, 1, 10m, 1250m, null, 0m),
                OrderLine(9002, 2, 5m, 280.5m, "D5", -5m),
                OrderLine(9003, 3, 20m, 45m, null, 0m),
            ],
        };
        var page = Open(lineCount: 3);

        Lines(page).Select(2);
        Button(page, "mark-delete").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-order-totals-subtotal]").TextContent.Should().Contain("13,832.38"));
        page.Find("[data-testid=sales-order-totals-tax]").TextContent.Should().Contain("1,383.24");
        page.Find("[data-testid=sales-order-totals-total]").TextContent.Should().Contain("15,215.62").And.Contain("USD");
        page.FindAll("[data-testid=sales-order-totals-discount]").Should().BeEmpty("the line amounts already take the discount");
        page.Field("sales-order-header-tot-base-amt").Value.Should().Be("21035595");
    }

    [Fact, Trait("Id", "SD-UC-002-T06"), Trait("Rule", "BC-BR-421")]
    public void KrwOrder_ShowsAmountsWithoutDecimals_AndNoBaseTotal()
    {
        _api.Localization = KoreanCompany;
        _api.Order = Order() with { CurrencyCode = "KRW", Lines = [OrderLine(9001, 1, 3m, 333m, null, 0m)] };

        var page = Open();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-order-totals-subtotal]").TextContent.Should().Contain("999"));
        page.Find("[data-testid=sales-order-totals-tax]").TextContent.Should().Contain("100");
        page.Find("[data-testid=sales-order-totals-total]").TextContent.Should().Contain("1,099").And.Contain("KRW");
        page.FindAll("[data-testid=sales-order-header-tot-base-amt]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UC-002-T07"), Trait("Rule", "SD-BR-003")]
    public async Task PartlyShippedOrder_TakesNoLineEdits()
    {
        _api.Order = Order("RE", 4);

        var page = Open();

        (await Lines(page).EditAsync(0, "soQty", "5")).Should().BeFalse();
        Frame(page).Lines.GetItem(Lines(page).KeyOf(0)).SoQty.Should().Be(10m);
    }

    [Fact, Trait("Id", "SD-UC-002-T08")]
    public async Task RestoringALine_KeepsTheHeaderChange_AndTheUnsavedMark()
    {
        var page = Open();
        var lines = Lines(page);
        page.Field("sales-order-header-remarks").Change("Changed remarks");
        await lines.EditAsync(0, "soQty", "12");
        var key = lines.KeyOf(0);
        page.WaitForAssertion(() => Frame(page).Lines.GetItem(key).SoQty.Should().Be(12m));

        lines.Select(0);
        Button(page, "restore-row").Click();

        Frame(page).Lines.GetItem(key).SoQty.Should().Be(10m);
        Frame(page).Lines.GetState(key).Should().Be(RowState.Normal);
        Frame(page).IsDirty.Should().BeTrue("the header change is not saved");
        Button(page, "save").HasAttribute("disabled").Should().BeFalse();
        page.Field("sales-order-header-remarks").Value.Should().Be("Changed remarks");
    }

    [Fact, Trait("Id", "SD-UC-002-T09"), Trait("Rule", "BC-BR-432")]
    public void KoreanCompany_HidesTheShipToState_AndDefaultsTheCountry()
    {
        _api.Localization = KoreanCompany;
        _api.Policies = [new("Sd.SalesOrder.ShipState", FieldPolicyStates.Hidden, null, "Sd.SalesOrder.ShipState", "CountryPack")];

        var page = RenderAs("admin");

        page.WaitForAssertion(() => page.FindAll("[data-testid=sales-order-header-ship-state]").Should().BeEmpty());
        page.Field("sales-order-header-ship-country").Value.Should().Be("KR");
        _api.Requests.Should().Contain(r => r.RequestUri!.ToString().Contains("Sd.SalesOrder", StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "SD-UC-002-T09")]
    public void WhileThePoliciesAreLoading_TheScreenDrawsANewDocument_AndAppliesThemWhenTheyArrive()
    {
        var answer = new TaskCompletionSource();
        _api.PoliciesArrive = answer.Task;
        _api.Policies = [new("Sd.SalesOrder.ShipState", FieldPolicyStates.Hidden, null, "Sd.SalesOrder.ShipState", "CountryPack")];

        var page = RenderAs("admin");

        page.Find("[data-testid=sales-order-doc-no]").TextContent.Should().Be("새 문서");
        page.FindAll("[data-testid=sales-order-header-ship-state]").Should().ContainSingle();

        answer.SetResult();

        // The late answer travels the HTTP pipeline on the thread pool, which is slow while the whole solution's tests run
        page.WaitForAssertion(() => page.FindAll("[data-testid=sales-order-header-ship-state]").Should().BeEmpty(), TimeSpan.FromSeconds(15));
        page.Find("[data-testid=sales-order-doc-no]").TextContent.Should().Be("새 문서");
    }

    [Fact, Trait("Id", "SD-UC-002-T09")]
    public void UsCompany_ShowsTheShipToState_AndDefaultsTheCountry()
    {
        var page = RenderAs("admin");

        page.FindAll("[data-testid=sales-order-header-ship-state]").Should().ContainSingle();
        page.Field("sales-order-header-ship-country").Value.Should().Be("US");
    }

    private IRenderedComponent<SalesOrder> RenderAs(string role, string? soNo = null)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<SalesOrder>(p => p.Add(x => x.SoNo, soNo));
        page.WaitForElement("[data-testid=sales-order-header-customer-name]");
        return page;
    }

    private IRenderedComponent<SalesOrder> Open(int lineCount = 1)
    {
        var page = RenderAs("admin", "SO2611000001");
        page.WaitForAssertion(() => page.Find("[data-testid=sales-order-doc-no]").TextContent.Should().Be("SO2611000001"));
        page.WaitForAssertion(() => Lines(page).Rows.Should().HaveCount(lineCount));
        return page;
    }

    private static AngleSharp.Dom.IElement ShipToBody(IRenderedComponent<SalesOrder> page) =>
        page.Find("[data-testid=sales-order-header-ship-to] .quro-field-section-body");

    private static void PickCustomer(IRenderedComponent<SalesOrder> page)
    {
        page.Find("[data-testid=sales-order-header-customer-lookup]").Click();
        page.WaitForAssertion(() => page.LookupRows("sales-order-customer-lookup").Should().NotBeEmpty());
        page.PickLookupRow<CustomerDto, SalesOrder>("sales-order-customer-lookup", 0);
    }

    private static DataGridPage<SalesOrderLine> Lines(IRenderedComponent<SalesOrder> page) =>
        page.DataGrid<SalesOrderLine, SalesOrder>("sales-order-line");

    private static QuroHeaderDetail<SalesOrderHeader, SalesOrderLine> Frame(IRenderedComponent<SalesOrder> page) =>
        page.FindComponent<QuroHeaderDetail<SalesOrderHeader, SalesOrderLine>>().Instance;

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<SalesOrder> page, string action) =>
        page.Find($"[data-testid={Screen}-toolbar-{action}]");

    private static AngleSharp.Dom.IElement FileInput(IRenderedComponent<SalesOrder> page, string action) =>
        page.Find($"[data-testid={Screen}-toolbar-{action}]");

    private static IReadOnlyList<string> Enabled(IRenderedComponent<SalesOrder> page) =>
        [.. page.FindAll("[role=toolbar] button:not([disabled])")
            .Select(b => b.GetAttribute("data-testid")!.Replace($"{Screen}-toolbar-", ""))
            .Where(b => b != "show-all-fields")];

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private static readonly LocalizationDto UsCompany = new("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "LINE", [],
        [new CurrencyDto("USD", "US Dollar", 2, "US"), new CurrencyDto("EUR", "Euro", 2, "DE")],
        [new CountryDecimalDto("US", "SD", 2)],
        [new DecimalSettingDto("SD", 2, "HALF_UP")]);

    private static readonly LocalizationDto KoreanCompany = new("KR", "KRW", "ko", "Asia/Seoul", "VAT", "yyyy-MM-dd", "LINE", [],
        [new CurrencyDto("KRW", "Won", 0, "KR"), new CurrencyDto("USD", "US Dollar", 2, "US")],
        [new CountryDecimalDto("KR", "SD", 0), new CountryDecimalDto("US", "SD", 2)],
        [new DecimalSettingDto("SD", 2, "HALF_UP")]);

    private static SalesOrderLineDto OrderLine(long id, int seq, decimal qty, decimal price, string? dcCd, decimal dcRate) => new(
        id, seq, 11, "BOLT", "Hex Bolt", qty, price, 0m, dcCd, dcRate, 12m, 1, 0m, 0m, 0m, 0m, null, "EA");

    private static SalesOrderDto Order(string status = "SO", decimal shipped = 0, decimal invoiced = 0, bool billingFlowLocked = false) => new(
        Id: 601, SoNo: "SO2611000001", Status: status, CustomerId: 801, CustomerName: "ACME", SoDt: new DateOnly(2026, 11, 4),
        ExpShipDt: new DateOnly(2026, 11, 6), DueDt: new DateOnly(2026, 12, 4), CurrencyCode: "USD", ExchangeRate: 1m,
        BillingFlow: "SHIP_THEN_BILL", BillingFlowLocked: billingFlowLocked, Rep: "KIM", Terms: "NET_30", ShipVia: "UPS", FreightCharge: "N",
        ShipName: "ACME DOCK", ShipAddr1: "1 Main St", ShipAddr2: null, ShipCity: "Springfield", ShipState: "CA", ShipZipCode: "90001",
        ShipCountry: "USA", ShipPhone: null, ShipFax: null, Remarks: "Rush", DcStatus: "D10", SupplyAmt: 900m, TaxAmt: 90m, TotSoAmt: 990m,
        TotBaseAmt: 990m,
        Lines: [new SalesOrderLineDto(9001, 1, 11, "BOLT", "Hex Bolt", 10m, 100m, 900m, "D10", -10m, 12m, 1, 900m, 90m, shipped, invoiced,
            invoiced > 0 ? "IV2611000001" : null, "EA")],
        UpdatedAt: AuditTimes.Updated, CreatedAt: AuditTimes.Created, CreatedBy: "setup", UpdatedBy: "kim");

    /// <summary>Sales order, customer, item, rate, localization, tax code and code API.</summary>
    private sealed class FakeSalesOrderApi : HttpMessageHandler
    {
        private static readonly CustomerDto Acme = new(
            Id: 801, CName: "ACME", ShipName: "ACME DOCK", Contact: "Kim", Contact2: null, Phone1: "555-0100", Phone2: null, Phone3: null,
            FaxNo: null, Email: null, Rep: "KIM", PriceLevel: "ACME", FreightCharge: "N", BalAmt: 70.5m, Remarks: null, Memo2: null,
            Memo3: null, Terms: "NET_30", ValidFlag: "Y", Addr1: "1 Main St", Addr2: null, City: "Springfield", State: "CA",
            Country: "USA", ZipCode: "90001", ShipAddr1: "1 Main St", ShipAddr2: null, ShipCity: "Springfield", ShipState: "CA",
            ShipCountry: "USA", ShipZipCode: "90001", ShipPhone: null, ShipFax: null, CreditLimit: 1000m, DcStatus: "D10",
            StartDt: new DateOnly(2026, 1, 5), BillingFlow: "SHIP_THEN_BILL", CurrencyCode: null, TaxCodeId: 1,
            TaxCode: "ST10", TaxPartyType: null, BusinessRegNo: null, CeoName: null, BusinessType: null, BusinessItem: null,
            CreatedAt: AuditTimes.Created, CreatedBy: "setup", UpdatedAt: AuditTimes.Updated, UpdatedBy: "kim");

        private static readonly ItemDto[] Items =
        [
            Item(11, "BOLT", "Hex Bolt", "EA", 12m),
            Item(12, "PANEL", "Panel", "EA", 25m),
        ];

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<SaveSalesOrderRequest> Saved { get; } = [];

        public SalesOrderDto Order { get; set; } = SalesOrderScreenTests.Order();

        /// <summary>The bodies of the next saves' errors, in order; a save without one passes.</summary>
        public Queue<string> SaveProblems { get; } = new();

        public SalesOrderLineFileResult FileResult { get; set; } = new([], []);

        public LocalizationDto Localization { get; set; } = UsCompany;

        public CustomerDto Customer { get; set; } = Acme;

        public IReadOnlyList<FieldPolicyDto> Policies { get; set; } = [];

        /// <summary>Holds the policy answer back, as a real API does while the page draws.</summary>
        public Task PoliciesArrive { get; set; } = Task.CompletedTask;

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
                await PoliciesArrive;
                return Json(Policies);
            }
            switch (path)
            {
                case "/" + LocalizationRoutes.Base:
                    return Json(Localization);
                case "/api/sd/customers/801":
                    return Json(Customer);
                case "/" + TaxCodeRoutes.Base:
                    return Json(new List<TaxCodeDto>
                    {
                        new(1, "ST10", "Sales tax 10%", new Dictionary<string, string>(), "SALES_TAX", 10m, null, null, false, true, true, AuditTimes.Updated),
                        new(2, "NONE", "Exempt", new Dictionary<string, string>(), "NONE", 0m, null, null, false, true, false, AuditTimes.Updated),
                    });
                case "/api/sd/sales-orders/discount-rates":
                    return Json(new List<SalesOrderDiscountRateDto> { new("D10", -10m), new("D5", -5m) });
                case "/api/sd/customers":
                    return Json(new CustomerListDto([Customer], 1));
                case "/" + ItemRoutes.Base:
                    var code = query["itemCd"];
                    var name = query["itemName"];
                    return Json(new ItemListDto([.. Items.Where(i =>
                        (string.IsNullOrEmpty(code) || i.ItemCd.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                        && (string.IsNullOrEmpty(name) || (i.ItemName ?? "").Contains(name, StringComparison.OrdinalIgnoreCase)))], Items.Length));
                case "/api/sd/sales-orders/line-defaults":
                    var item = Items.First(i => i.ItemCd.Equals(query["itemCd"], StringComparison.OrdinalIgnoreCase));
                    return Json(new SalesOrderLineDefaultsDto(item.Id, item.ItemCd, item.ItemName, item.Unit, 1m, 9.5m, item.SoUsdPrc, "D10", -10m,
                        query["shipState"] == "NY" ? 2 : 1, null));
                case "/api/sd/sales-orders/line-file":
                    return Json(FileResult);
                case "/api/sd/sales-orders" when request.Method == HttpMethod.Get:
                    // The server filters by the customer name; the screen shows what comes back
                    return Json(string.IsNullOrEmpty(query["customerName"]) || (Order.CustomerName ?? "").Contains(query["customerName"]!, StringComparison.OrdinalIgnoreCase)
                        ? new List<SalesOrderSummaryDto>
                        {
                            new(Order.Id, Order.SoNo, Order.Status, Order.SoDt, Order.CustomerId, Order.CustomerName, Order.CurrencyCode, Order.TotSoAmt),
                        }
                        : []);
                case "/api/sd/sales-orders" when request.Method == HttpMethod.Post:
                case "/api/sd/sales-orders/601" when request.Method == HttpMethod.Put:
                    Saved.Add((await request.Content!.ReadFromJsonAsync<SaveSalesOrderRequest>(cancellationToken))!);
                    if (SaveProblems.TryDequeue(out var problem))
                    {
                        var status = problem.Contains("\"status\":409", StringComparison.Ordinal) ? HttpStatusCode.Conflict : HttpStatusCode.UnprocessableEntity;
                        return new HttpResponseMessage(status) { Content = new StringContent(problem, null, "application/problem+json") };
                    }
                    var saved = Saved[^1];
                    return Json(Order with
                    {
                        Lines = [.. saved.Lines.Select((l, i) => new SalesOrderLineDto(
                            l.Id ?? 9100 + i, i + 1, l.ItemId, Items.First(x => x.Id == l.ItemId).ItemCd, l.ItemName, l.SoQty ?? 1, l.SoPrc ?? 0,
                            0m, l.DcCd, 0m, null, l.TaxCodeId ?? 1, 0m, 0m, 0m, 0m, null))],
                    });
                case "/api/sd/sales-orders/601":
                    return Json(Order);
                case "/api/sd/sales-orders/601/print/sales-order.pdf":
                case "/api/sd/sales-orders/601/print/picking-list.pdf":
                case "/api/sd/sales-orders/601/print/set-component-list.pdf":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF-1.7"u8.ToArray()) };
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static IReadOnlyList<CodeItemDto> Codes(string major) => major switch
        {
            "SO_STATUS" => [new("SO", "수주"), new("RE", "부분 출하"), new("CL", "마감")],
            "TERMS" => [new("COD_CHECK", "COD 수표"), new("NET_30", "NET 30")],
            "STATE" => [new("CA", "California"), new("NY", "New York")],
            "SALES_REP" => [new("KIM", "Kim")],
            "SHIPMENT_VIA" => [new("UPS", "UPS")],
            "FREIGHT_CHARGE" => [new("Y", "Y"), new("N", "N")],
            "DISCOUNT_STATUS" => [new("D10", "10%"), new("D5", "5%")],
            _ => [],
        };

        private static ItemDto Item(long id, string code, string name, string unit, decimal salesPrice) => new(
            id, code, name, null, null, "INVENTORY", unit, null, "Y", 0m, salesPrice, null, null, 0m, null, null, null, null, null, null, true,
            "TAXABLE", "", 0m, 0m, 0m, AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

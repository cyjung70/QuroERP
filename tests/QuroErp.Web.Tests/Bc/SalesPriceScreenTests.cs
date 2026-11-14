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
using QuroErp.Contracts.Bc.SalesPrices;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.SalesPrices;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Pages.Bc.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Sales price screen /bc/sales-price (BC-UC-014, TASK-BC-028) against a fake item and sales price API.</summary>
public class SalesPriceScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public SalesPriceScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(new FakeConfirm());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<SalesPriceApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
        // Screens ask IScreenAccess, which reads the role from this test authorization (security.md "화면 쓰기 권한")
        Services.AddScoped<IScreenAccess, ScreenAccess>();
    }

    [Fact]
    public void Open_ShowsTheSpecColumns_AndTheRequiredPriceLevel_AndSearchesNothingYet()
    {
        var page = RenderAs("admin");

        Grid(page).Headers.Select(h => h.Title).Should().ContainInOrder("품목 코드", "품목 이름", "판매 단가", "표준 판매가");
        page.Find("[data-testid=sales-price-search-price-level]").GetAttribute("aria-required").Should().Be("true");
        _api.Reads.Should().BeEmpty();
    }

    [Fact, Trait("Id", "SD-UT-409")]
    public void Search_WithoutAPriceLevel_AsksForIt()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=sales-price-toolbar-search]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-price-message]").TextContent.Should().Be("가격 레벨을(를) 입력하세요."));
        _api.Reads.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-014-T03"), Trait("Id", "SD-UT-409")]
    public void Search_InLowerCase_ShowsTheLevelRows_WithTheStandardPrice_InTheSdPriceDecimals()
    {
        var page = SearchDealer(RenderAs("admin"));

        Grid(page).Column("itemCd").Should().Equal("BOLT", "NUT");
        Grid(page).Column("itemName").Should().Equal("Bolt", "Nut");
        Grid(page).Column("salesPrc").Should().Equal("0.750", "0.300");
        Grid(page).Column("stdPrc").Should().Equal("0.900", "");
        Grid(page).IsReadOnly(0, "stdPrc").Should().BeTrue();
        _api.Reads.Should().Equal("DLR");
    }

    [Fact, Trait("Id", "SD-UT-408")]
    public void TypedItemCode_FillsTheNameAndStandardPrice_AndAnItemThatCannotHaveAPriceIsNotTaken()
    {
        var page = SearchDealer(RenderAs("admin"));
        page.Find("[data-testid=sales-price-toolbar-add-row]").Click();

        Grid(page).Edit(2, "itemCd", "washer").Should().BeTrue();

        Grid(page).Text(2, "itemCd").Should().Be("WASHER");
        Grid(page).Text(2, "itemName").Should().Be("Washer");
        Grid(page).Text(2, "stdPrc").Should().Be("0.200");
        // Service items and items not in use have no level price (SD-BR-408): no match keeps the cell in edit
        Grid(page).Edit(2, "itemCd", "FREIGHT").Should().BeFalse();
        Grid(page).Edit(2, "itemCd", "OLDB").Should().BeFalse();
        Grid(page).Text(2, "itemCd").Should().Be("WASHER");
    }

    [Fact, Trait("Id", "BC-UC-014-T01"), Trait("Id", "SD-UT-409")]
    public void AddChangeAndDelete_AreSavedTogether_ForTheLevelThatWasRead()
    {
        var page = SearchDealer(RenderAs("admin"));

        Grid(page).Edit(1, "salesPrc", "0.35");
        Grid(page).Select(0);
        page.Find("[data-testid=sales-price-toolbar-mark-delete]").Click();
        Grid(page).Select(1);
        page.Find("[data-testid=sales-price-toolbar-add-row]").Click();
        Grid(page).Edit(2, "itemCd", "WASHER");
        Grid(page).Edit(2, "salesPrc", "0.15");
        page.Find("[data-testid=sales-price-search-price-level]").Change("RTL");

        page.Find("[data-testid=sales-price-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-price-message]").TextContent.Should().Be("저장했습니다."));
        var (level, request) = _api.Saved.Should().ContainSingle().Subject;
        level.Should().Be("DLR");
        request.Added.Should().Equal(new SalesPriceRowRequest(null, "WASHER", 0.15m, null));
        request.Updated.Should().Equal(new SalesPriceRowRequest(12, "NUT", 0.35m, Read));
        request.Deleted.Should().Equal(new SalesPriceDeleteRequest(11, Read));
    }

    [Theory, Trait("Id", "BC-UC-014-T02"), Trait("Id", "SD-UT-403"), Trait("Id", "SD-UT-407")]
    [InlineData("""{"status":422,"code":"SD-E-407","args":["NUT"]}""", "품목 NUT의 판매 단가는 0보다 커야 합니다.")]
    [InlineData("""{"status":422,"code":"SD-E-403","args":["DLR","NUT"]}""", "가격 레벨 DLR / 품목 NUT:")]
    public void RejectedRow_IsNamedInTheMessage_AndSelected_AndTheGridKeepsTheChanges(string problem, string message)
    {
        _api.SaveProblem = (HttpStatusCode.UnprocessableEntity, problem);
        var page = SearchDealer(RenderAs("admin"));
        Grid(page).Edit(1, "salesPrc", "0.5");
        Grid(page).Select(0);

        page.Find("[data-testid=sales-price-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-price-message]").TextContent.Should().StartWith(message));
        var data = page.FindComponent<QuroMultiGrid<SalesPriceRow>>().Instance.Data;
        data.GetItem(data.CurrentRowKey!.Value).ItemCd.Should().Be("NUT");
        Grid(page).Column("salesPrc")[1].Should().Be("0.500");
    }

    [Fact, Trait("Id", "SD-UT-409")]
    public void StaleRow_Shows409Message()
    {
        _api.SaveProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");
        var page = SearchDealer(RenderAs("admin"));
        Grid(page).Edit(0, "salesPrc", "0.8");

        page.Find("[data-testid=sales-price-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=sales-price-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "SD-UT-409")]
    public void CustomerPriceLevel_LongerThan5_IsSearchedInUpperCase()
    {
        var page = RenderAs("admin");
        var level = page.Find("[data-testid=sales-price-search-price-level]");
        level.GetAttribute("maxlength").Should().Be("100");

        level.Change(" Blue River Market ");
        page.Find("[data-testid=sales-price-toolbar-search]").Click();

        page.WaitForAssertion(() => _api.Reads.Should().Equal("BLUE RIVER MARKET"));
        page.Find("[data-testid=sales-price-search-price-level]").GetAttribute("value").Should().Be("BLUE RIVER MARKET");
    }

    [Fact]
    public void AddRow_WaitsForASearchedLevel()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=sales-price-search-price-level]").Change("DLR");

        page.Find("[data-testid=sales-price-toolbar-add-row]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void New_ClearsTheLevelAndTheGrid_AndAddRowWaitsAgain()
    {
        var page = SearchDealer(RenderAs("admin"));

        page.Find("[data-testid=sales-price-toolbar-new]").Click();

        page.WaitForAssertion(() => Grid(page).Column("itemCd").Should().BeEmpty());
        page.Find("[data-testid=sales-price-search-price-level]").GetAttribute("value").Should().BeNullOrEmpty();
        page.Find("[data-testid=sales-price-toolbar-add-row]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void RestoreRow_ReturnsTheReadPrice()
    {
        var page = SearchDealer(RenderAs("admin"));
        Grid(page).Edit(0, "salesPrc", "0.8");
        Grid(page).Select(0);

        page.Find("[data-testid=sales-price-toolbar-restore-row]").Click();

        page.WaitForAssertion(() => Grid(page).Column("salesPrc")[0].Should().Be("0.750"));
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutFullAccess_CanOnlyRead_NewSaveAndRowCommandsAreDisabledNotHidden()
    {
        var page = SearchDealer(RenderAs("user"));
        Grid(page).Select(0);

        page.Find("[data-testid=sales-price-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=sales-price-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=sales-price-toolbar-add-row]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=sales-price-toolbar-mark-delete]").HasAttribute("disabled").Should().BeTrue();
        Grid(page).IsReadOnly(0, "salesPrc").Should().BeTrue();
        Grid(page).Edit(0, "salesPrc", "9").Should().BeFalse();
        _api.Saved.Should().BeEmpty();
    }

    [Fact]
    public void English_ShowsTheEnglishTitleAndColumns()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = RenderAs("admin");

        Grid(page).Headers.Select(h => h.Title).Should().ContainInOrder("Item code", "Item name", "Sales price", "Standard price");
        page.Markup.Should().Contain("Sales Prices");
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<SalesPrice> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<SalesPrice>();
        page.WaitForElement("[data-testid=sales-price-search-price-level]");
        return page;
    }

    private static IRenderedComponent<SalesPrice> SearchDealer(IRenderedComponent<SalesPrice> page)
    {
        page.Find("[data-testid=sales-price-search-price-level]").Change("dlr");
        page.Find("[data-testid=sales-price-toolbar-search]").Click();
        page.WaitForAssertion(() => Grid(page).Column("itemCd").Should().NotBeEmpty());
        return page;
    }

    private static DataGridPage<SalesPriceRow> Grid(IRenderedComponent<SalesPrice> page) =>
        page.DataGrid<SalesPriceRow, SalesPrice>("sales-price-grid");

    /// <summary>
    /// SD unit prices with 3 decimals. Level DLR has BOLT 0.75 and NUT 0.3 (no standard price); items BOLT, NUT and WASHER can
    /// have a level price, FREIGHT (service) and OLDB (not in use) cannot.
    /// </summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        private static readonly ItemDto[] Items =
        [
            Item(1, "BOLT", "Bolt", "INVENTORY", "Y", 0.9m),
            Item(2, "FREIGHT", "Freight", "SERVICE", "Y", 75m),
            Item(7, "NUT", "Nut", "INVENTORY", null, 0),
            Item(8, "OLDB", "Old bolt", "INVENTORY", "N", 1m),
            Item(9, "WASHER", "Washer", "INVENTORY", "Y", 0.2m),
        ];

        private static readonly List<SalesPriceDto> Dealer =
        [
            new(11, 1, "BOLT", "Bolt", 0.75m, 0.9m, Read),
            new(12, 7, "NUT", "Nut", 0.3m, null, Read),
        ];

        public List<string> Reads { get; } = [];

        public List<(string Level, BatchRequest<SalesPriceRowRequest, SalesPriceDeleteRequest> Request)> Saved { get; } = [];

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT", [new("en", 1)], [],
                    [new("US", "SD", 3)], [new("SD", 2, "HALF_UP")]));
            }
            if (path == "/" + ItemRoutes.Base)
            {
                var code = query["itemCd"];
                var name = query["itemName"];
                var found = Items.Where(i => (code is null || i.ItemCd.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                                             && (name is null || i.ItemName!.Contains(name, StringComparison.OrdinalIgnoreCase))).ToList();
                return Json(new ItemListDto(found, found.Count));
            }
            if (path == $"/{SalesPriceRoutes.Base}/{SalesPriceRoutes.Batch}")
            {
                if (SaveProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                var body = await request.Content!.ReadFromJsonAsync<BatchRequest<SalesPriceRowRequest, SalesPriceDeleteRequest>>(cancellationToken);
                Saved.Add((query["priceLevel"]!, body!));
                return Json(Dealer);
            }
            if (path != "/" + SalesPriceRoutes.Base)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            var level = query["priceLevel"]!;
            Reads.Add(level);
            return Json(level == "DLR" ? Dealer : []);
        }

        private static ItemDto Item(long id, string itemCd, string name, string type, string? validFlag, decimal soPrice) =>
            new(id, itemCd, name, null, null, type, "EA", "C1", validFlag, 0, soPrice, null, null, 0, null, null, null, null, null, null, false,
                "TAXABLE", "", 0, 0, 0, Read, "t", Read, "t");

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

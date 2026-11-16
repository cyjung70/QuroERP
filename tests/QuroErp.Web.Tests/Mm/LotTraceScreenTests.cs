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
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Lots;
using QuroErp.Web.Sd.Customers;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Lot trace screen /mm/lot-trace (MM-UC-040, TASK-MM-063) against a fake API.</summary>
public class LotTraceScreenTests : BunitContext
{
    private readonly FakeApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public LotTraceScreenTests()
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
        Services.AddHttpClient<LotTraceApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CustomerApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<IScreenAccess, ScreenAccess>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "MM-UC-040-T01"), Trait("Rule", "MM-BR-366")]
    public void Forward_ItemAndLot_ShowTheTree_WithTheKindInTheUserLanguage()
    {
        var page = RenderAs("admin");

        SearchForward(page);

        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(3));
        _api.Queries.Should().Equal("/api/mm/lot-trace/forward?itemId=12&lotNo=261104-001");
        Grid(page).Column("docTypeText").Should().Equal("Lot", "입고 (GR)", "출하 (DN)");
        Grid(page).Column("docNoText").Should().Equal("GR-001-1", "GR-001-1", "SH-001-1");
        Grid(page).Text(2, "partyName").Should().Be("Customer A");
        Grid(page).Text(2, "qty").Should().Be("-3.00");
        page.FindAll("[role=treegrid]").Should().NotBeEmpty("the rows show as a tree");
        Grid(page).Headers.Select(h => h.Title).Should().ContainInOrder("구분", "문서 번호", "일자", "창고", "거래처", "품목 코드", "품명", "Lot 번호", "수량");
    }

    [Fact, Trait("Rule", "MM-BR-366")]
    public void Forward_WithoutAnItem_AsksForIt_AndCallsNothing()
    {
        var page = RenderAs("admin");

        page.Field("lot-trace-search-lot-no").Change("261104-001");
        page.Find("[data-testid=lot-trace-toolbar-search]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=lot-trace-message]").TextContent.Should().Contain("품목").And.Contain("입력하세요"));
        _api.Queries.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UC-040-T02"), Trait("Rule", "MM-BR-367")]
    public void Backward_DocumentNumber_IsSentInUpperCase()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=lot-trace-direction-backward]").Change(true);
        page.Field("lot-trace-search-doc-no").Change("sh-001");
        page.Find("[data-testid=lot-trace-toolbar-search]").Click();

        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(2));
        _api.Queries.Should().Equal("/api/mm/lot-trace/backward?docNo=SH-001");
        Grid(page).Column("docTypeText").Should().Equal("출하 (DN)", "입고");
    }

    [Fact, Trait("Rule", "MM-BR-367")]
    public void Backward_WithoutANumberOrACustomer_AsksForTheNumber()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=lot-trace-direction-backward]").Change(true);
        page.Find("[data-testid=lot-trace-toolbar-search]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=lot-trace-message]").TextContent.Should().Contain("출하·송장 번호"));
        _api.Queries.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UC-040-T04"), Trait("Rule", "MM-BR-369")]
    public void Print_SendsTheConditionsOfTheShownRows_ForThePdf()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=lot-trace-toolbar-print]").Click();
        page.WaitForAssertion(() => page.Find("[data-testid=lot-trace-message-print]").TextContent.Should().Be("먼저 조회하세요."));

        SearchForward(page);
        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(3));
        page.Field("lot-trace-search-lot-no").Change("OTHER");
        page.Find("[data-testid=lot-trace-toolbar-print]").Click();

        page.WaitForAssertion(() => _api.Files.Should().Equal("/api/mm/lot-trace/forward/export.pdf?itemId=12&lotNo=261104-001"));
        page.FindAll("[data-testid=lot-trace-message-print]").Should().BeEmpty();
    }

    [Fact]
    public void English_ShowsTheLabelsAndKindsInEnglish()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");
        var page = RenderAs("admin");

        SearchForward(page);

        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(3));
        Grid(page).Column("docTypeText").Should().Equal("Lot", "Goods Receipt (GR)", "Shipment (DN)");
        Grid(page).Headers.Select(h => h.Title).Should().Contain(["Type", "Document No.", "Party", "Lot No."]);
        page.Find("[data-testid=lot-trace-search-direction]").TextContent.Should().Contain("Forward").And.Contain("Backward");
    }

    [Fact]
    public void EveryInputButtonAndGrid_HasTestIdByRule()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=lot-trace-direction-backward]").Change(true);

        var elements = page.FindAll("input, select, textarea, button:not([hidden]), .quro-grid");
        elements.Should().NotBeEmpty();
        elements.Select(DataGridPageExtensions.TestIdOf).Should()
            .OnlyContain(id => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^lot-trace(-(search|grid|toolbar|message|direction)(-[a-z0-9]+)*)?$"));
    }

    private static void SearchForward(IRenderedComponent<LotTrace> page)
    {
        page.Find("[data-testid=lot-trace-search-item-lookup]").Click();
        page.WaitForAssertion(() => page.LookupRows("lot-trace-search-item-dialog").Should().NotBeEmpty());
        page.PickLookupRow<ItemDto, LotTrace>("lot-trace-search-item-dialog", 0);
        page.Field("lot-trace-search-lot-no").Change("261104-001");
        page.Find("[data-testid=lot-trace-toolbar-search]").Click();
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<LotTrace> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<LotTrace>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=lot-trace-grid]").Should().NotBeEmpty());
        return page;
    }

    private static DataGridPage<LotTraceRow> Grid(IRenderedComponent<LotTrace> page) => page.DataGrid<LotTraceRow, LotTrace>("lot-trace-grid");

    /// <summary>Item CABLE (12) with Lot 261104-001 from GR-001 of Vendor A, 3 shipped to Customer A on SH-001; MM qty 2 decimals.</summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        private static readonly DateTimeOffset Updated = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateOnly Day = new(2026, 11, 4);

        public List<string> Queries { get; } = [];

        public List<string> Files { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Task.FromResult(Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "LINE", [],
                    [new CurrencyDto("USD", "US Dollar", 2, "US")], [new CountryDecimalDto("US", "MM", 2)], [new DecimalSettingDto("MM", 2, "HALF_UP")])));
            }
            if (path == "/" + ItemRoutes.Base)
            {
                return Task.FromResult(Json(new ItemListDto([Item(12, "CABLE", "Cable Reel")], 1)));
            }
            if (path.EndsWith(".pdf", StringComparison.Ordinal) || path.EndsWith(".xlsx", StringComparison.Ordinal))
            {
                Files.Add(uri.PathAndQuery);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
            }
            if (path == "/api/mm/lot-trace/forward")
            {
                Queries.Add(uri.PathAndQuery);
                return Task.FromResult(Json(new List<LotTraceRowDto>
                {
                    new(1, null, 0, "LOT", "GR-001", 1, Day, null, null, "Vendor A", "CABLE", "Cable Reel", "261104-001", null),
                    new(2, 1, 1, "GR", "GR-001", 1, Day, "GR", "MAIN", "Vendor A", "CABLE", "Cable Reel", "261104-001", 10m),
                    new(3, 1, 1, "SHIP", "SH-001", 1, Day.AddDays(1), "DN", "MAIN", "Customer A", "CABLE", "Cable Reel", "261104-001", -3m),
                }));
            }
            if (path == "/api/mm/lot-trace/backward")
            {
                Queries.Add(uri.PathAndQuery);
                return Task.FromResult(Json(new List<LotTraceRowDto>
                {
                    new(1, null, 0, "SHIP", "SH-001", 1, Day.AddDays(1), "DN", "MAIN", "Customer A", "CABLE", "Cable Reel", "261104-001", -3m),
                    new(2, 1, 1, "GR", "GR-001", 1, Day, null, null, "Vendor A", "CABLE", "Cable Reel", "261104-001", 10m),
                }));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static ItemDto Item(long id, string code, string name) => new(
            id, code, name, null, null, "INVENTORY", "EA", null, "Y", 0m, 10m, null, null, 0m, null, null, null, null, null, null, true,
            "TAXABLE", "", 0m, 0m, 0m, Updated, "setup", Updated, "kim");

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

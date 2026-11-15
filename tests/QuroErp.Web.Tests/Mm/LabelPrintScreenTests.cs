using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Mm;
using QuroErp.Web.Components.Pages.Mm.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.GoodsReceipts;
using QuroErp.Web.Mm.Labels;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Mm;

/// <summary>Barcode label print screen /mm/label-print (MM-UC-037, TASK-MM-058) against a fake API.</summary>
public class LabelPrintScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public LabelPrintScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<LabelPrintApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LabelFormatApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<GoodsReceiptApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<ItemApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddScoped<IScreenAccess, ScreenAccess>();
        _auth = AddAuthorization();
    }

    [Fact, Trait("Rule", "MM-BR-917")]
    public void Open_ShowsTheSpecColumns_PickAndCopiesEditable_AndTheOutputKindOfThePalletFormat()
    {
        var page = Search(RenderAs());
        var grid = Grid(page);

        grid.Headers.Select(h => h.Title).Should().ContainInOrder(
            "선택", "매수", "코드", "이름", "품목 코드", "Lot", "수량", "유효기한", "창고", "Location");
        grid.Column("code").Should().Equal("PL2611000001", "PL2611000002");
        grid.Column("copies").Should().Equal("1", "1");
        grid.IsReadOnly(0, "selected").Should().BeFalse();
        grid.IsReadOnly(0, "copies").Should().BeFalse();
        grid.IsReadOnly(0, "code").Should().BeTrue();
        page.Find("[data-testid=label-print-toolbar-output-kind]").GetAttribute("value").Should().Be("PDF", "the PALLET format prints PDF");
        page.FindAll("[data-testid=label-print-toolbar-save]").Should().BeEmpty("nothing on the grid is saved");
        page.FindAll("[data-testid=label-print-toolbar-add-row]").Should().BeEmpty();
    }

    [Fact]
    public void Search_FindsTheReceiptByItsNumber_AndAnUnknownNumberIsNotFound()
    {
        var page = RenderAs();

        page.Field("label-print-search-gr-no").Change("gr2611000005");
        page.Find("[data-testid=label-print-toolbar-search]").Click();
        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(2));
        page.Field("label-print-search-gr-no").Change("GR0000000000");
        page.Find("[data-testid=label-print-toolbar-search]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=label-print-message]").TextContent.Should().Contain("GR0000000000"));
        _api.Queries.Should().Equal("?labelType=PALLET&goodsReceiptId=5");
    }

    [Fact, Trait("Rule", "MM-BR-917")]
    public void ALocationList_TakesItsFormatsOutputKind_AndTheWarehouse()
    {
        var page = RenderAs();

        page.Field("label-print-search-label-type").Change("LOCATION");
        page.Field("label-print-search-warehouse").Change("1");
        page.Find("[data-testid=label-print-toolbar-search]").Click();

        page.WaitForAssertion(() => Grid(page).Column("code").Should().Equal("A-01"));
        _api.Queries.Should().Equal("?labelType=LOCATION&warehouseId=1");
        page.Find("[data-testid=label-print-toolbar-output-kind]").GetAttribute("value").Should().Be("ZPL");
    }

    [Fact, Trait("Id", "MM-UC-037-T01"), Trait("Id", "MM-UC-037-T03"), Trait("Rule", "MM-BR-918")]
    public void Print_SendsThePickedRowsWithTheirCopies_InGridOrder_AndSavesTheFile()
    {
        var page = Search(RenderAs());
        var grid = Grid(page);
        page.Find("[data-testid=label-print-toolbar-print]").HasAttribute("disabled").Should().BeTrue("no row is picked");

        grid.Toggle(1, "selected").Should().BeTrue();
        grid.Edit(1, "copies", "3").Should().BeTrue();
        grid.Toggle(0, "selected").Should().BeTrue();
        page.Find("[data-testid=label-print-toolbar-print]").Click();

        page.WaitForAssertion(() => _api.Prints.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new LabelPrintRequest("PALLET", [new LabelPrintTarget(1, 1), new LabelPrintTarget(2, 3)], "PDF", null)));
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "quroDownload"
            && ((string)i.Arguments[0]!).StartsWith("labels-pallet-", StringComparison.Ordinal)
            && ((string)i.Arguments[0]!).EndsWith(".pdf", StringComparison.Ordinal));
        page.Find("[data-testid=label-print-print-notice]").TextContent.Should().StartWith("2건을 labels-pallet-");
    }

    [Fact, Trait("Rule", "MM-BR-919")]
    public void Print_AsZpl_SavesTheText_AndARefusedPrintShowsTheApiMessage()
    {
        var page = Search(RenderAs());
        Grid(page).Toggle(0, "selected");
        page.Find("[data-testid=label-print-toolbar-output-kind]").Change("ZPL");
        page.Find("[data-testid=label-print-toolbar-print]").Click();
        page.WaitForAssertion(() => _api.Prints.Should().ContainSingle().Which.OutputKind.Should().Be("ZPL"));
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "quroDownload" && ((string)i.Arguments[0]!).EndsWith(".zpl", StringComparison.Ordinal));

        Grid(page).Edit(0, "copies", "0");
        page.Find("[data-testid=label-print-toolbar-print]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=label-print-print-message]").TextContent.Should().Contain("copies"));
    }

    [Fact]
    public void PickedRows_AreNotAChangeToConfirm_BeforeASearch()
    {
        var page = Search(RenderAs());
        Grid(page).Toggle(0, "selected");

        page.Find("[data-testid=label-print-toolbar-search]").Click();

        page.WaitForAssertion(() => _api.Queries.Should().HaveCount(2));
        _confirm.Questions.Should().BeEmpty();
    }

    // security.md "화면 쓰기 권한": a user without Full Access can search but not pick or print
    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutWriteAccess_CanOnlySearch()
    {
        var access = new FakeScreenAccess(false);
        Services.AddSingleton<IScreenAccess>(access);

        var page = Search(RenderAs());

        Grid(page).IsReadOnly(0, "selected").Should().BeTrue();
        Grid(page).IsReadOnly(0, "copies").Should().BeTrue();
        page.Find("[data-testid=label-print-toolbar-print]").HasAttribute("disabled").Should().BeTrue();
        access.Asked.Should().Equal("/mm/label-print");
    }

    [Fact]
    public void English_ShowsTheEnglishLabels()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

        var page = Search(RenderAs());

        Grid(page).Headers.Select(h => h.Title).Should().Contain(["Select", "Copies", "Expiry Date"]);
        page.Find("[data-testid=label-print-toolbar-print]").TextContent.Should().Be("Print");
        page.FindAll("[data-testid=label-print-search-label-type] option").Select(o => o.TextContent).Should().Contain(["Pallet", "Item"]);
    }

    [Fact]
    public void EveryInputButtonAndGrid_HasTestIdByRule()
    {
        var page = Search(RenderAs());

        var elements = page.FindAll("input, select, textarea, button:not([hidden]), .quro-grid");
        elements.Should().NotBeEmpty();
        elements.Select(DataGridPageExtensions.TestIdOf).Should()
            .OnlyContain(id => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^label-print(-(search|grid|toolbar|message|print)(-[a-z0-9]+)*)?$"));
    }

    [Fact]
    public void ToRequest_SendsOnlyThePickedRows_AndCopiesThatAreNotAWholeNumberAsZero()
    {
        LabelPrintRow[] rows =
        [
            new() { Id = 1, Selected = true, Copies = 2 },
            new() { Id = 2, Selected = false, Copies = 5 },
            new() { Id = 3, Selected = true, Copies = 1.5m },
            new() { Id = 4, Selected = true, Copies = null },
        ];

        var request = LabelPrintRow.ToRequest("ITEM", rows, null);

        request.Targets.Should().Equal(new LabelPrintTarget(1, 2), new LabelPrintTarget(3, 0), new LabelPrintTarget(4, 0));
        request.LabelType.Should().Be("ITEM");
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<LabelPrint> RenderAs()
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse("token-admin", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "admin", false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles("admin");
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<LabelPrint>();
        page.WaitForAssertion(() => page.Find("[data-testid=label-print-toolbar-search]"));
        return page;
    }

    // The Pallets of the default search (PALLET, no other condition)
    private static IRenderedComponent<LabelPrint> Search(IRenderedComponent<LabelPrint> page)
    {
        page.Find("[data-testid=label-print-toolbar-search]").Click();
        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(2));
        return page;
    }

    private static DataGridPage<LabelPrintRow> Grid(IRenderedComponent<LabelPrint> page) =>
        page.DataGrid<LabelPrintRow, LabelPrint>("label-print-grid");

    /// <summary>
    /// Formats: PALLET prints PDF, the others ZPL. Receipt GR2611000005 (5) made Pallets PL2611000001 (1) and PL2611000002 (2);
    /// warehouse MAIN (1) has Location A-01 (30). A print with copies 0 is refused as the API does.
    /// </summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        private static readonly DateOnly Expiry = new(2027, 3, 31);

        public List<string> Queries { get; } = [];

        public List<LabelPrintRequest> Prints { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri.Query;
            if (path == "/" + WarehouseRoutes.Options)
            {
                return Json(new List<WarehouseOptionDto> { new(1, "MAIN", "Main", "NORMAL", true) });
            }
            if (path == "/" + LabelFormatRoutes.Base)
            {
                return Json(new List<LabelFormatDto>
                {
                    new("PALLET", "PDF", 100, 150, 203, ["palletNo"], ["palletNo"], true, Read),
                    new("ITEM", "ZPL", 50, 25, 203, ["itemCd"], ["itemCd"], true, Read),
                    new("LOT", "ZPL", 50, 25, 203, ["lotNo"], ["lotNo"], true, Read),
                    new("LOCATION", "ZPL", 100, 50, 203, ["locationCode"], ["locationCode"], true, Read),
                });
            }
            if (path == "/" + GoodsReceiptRoutes.Base)
            {
                var number = System.Web.HttpUtility.ParseQueryString(query)["grNo"];
                return Json(number == "GR2611000005"
                    ? new List<GoodsReceiptSummaryDto> { new(5, "GR2611000005", "CL", new DateOnly(2026, 11, 1), 3, "Vendor", "KRW", 0) }
                    : []);
            }
            if (path == "/" + LabelPrintRoutes.Targets)
            {
                Queries.Add(query);
                var labelType = System.Web.HttpUtility.ParseQueryString(query)["labelType"];
                return Json(labelType == "LOCATION"
                    ? new List<LabelTargetDto> { new(30, "LOCATION", "A-01", "Shelf A1", null, null, null, null, "MAIN", "A-01") }
                    : [
                        new(1, "PALLET", "PL2611000001", "Hex Bolt", "BOLT", "L1", 6, Expiry, "MAIN", "A-01"),
                        new(2, "PALLET", "PL2611000002", "Hex Bolt", "BOLT", "L1", 4, Expiry, "MAIN", "A-01"),
                    ]);
            }
            if (path == "/" + LabelPrintRoutes.Base && request.Method == HttpMethod.Post)
            {
                var print = (await request.Content!.ReadFromJsonAsync<LabelPrintRequest>(cancellationToken))!;
                if (print.Targets!.Any(t => t.Copies < 1))
                {
                    return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                    {
                        Content = new StringContent("""{"status":422,"code":"Common-E-005","args":["copies"]}""", null, "application/problem+json"),
                    };
                }
                Prints.Add(print);
                return print.OutputKind == "ZPL"
                    ? Json(new LabelPrintZplDto("ZPL", "^XA^XZ\n"))
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } } };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

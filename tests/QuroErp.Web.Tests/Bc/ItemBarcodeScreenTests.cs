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
using QuroErp.Contracts.Common.Codes;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Items;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Item barcode screen /bc/item-barcode (BC-UC-011, TASK-BC-022) against a fake item barcode, code and localization API.</summary>
public class ItemBarcodeScreenTests : BunitContext
{
    private readonly FakeBarcodeApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public ItemBarcodeScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<ItemBarcodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "BC-UC-011-T01")]
    public void Search_SendsItemClassAndValidFlag_AndShowsTheRowsWithPriceDecimals()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=item-barcode-search-valid-flag-y]").HasAttribute("checked").Should().BeTrue();
        Value(page, "item-barcode-search-start-no").Should().Be("1000000000");

        Search(page);

        Rows(page).Select(r => string.Join('|', r)).Should().Equal("A100|APPLE|4.0000|1000000005", "B200|BOLT|1.5000|");
        _api.Requests.Should().Contain("/api/bc/item-barcodes?itemClass=FR&validFlag=Y");
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("item-barcode-toolbar-search", "item-barcode-toolbar-export");
    }

    [Fact, Trait("Id", "BC-UC-011-T06")]
    public void SearchOrGenerate_WithoutItemClass_ShowsTheRequiredMessage_AndCallsNothing()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=item-barcode-search-generate]").Click();

        page.WaitForAssertion(() => Message(page).Should().Contain("품목군"));
        _api.Requests.Should().NotContain(r => r.StartsWith("/api/bc/item-barcodes", StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "BC-UC-011-T02"), Trait("Id", "MM-UT-815")]
    public void Generate_AsksWithTheCounts_ThenGenerates_AndShowsTheNewBarcodes()
    {
        var page = RenderAs("admin");
        Search(page);
        page.Find("[data-testid=item-barcode-search-start-no]").Change("2000");

        page.Find("[data-testid=item-barcode-search-generate]").Click();

        page.WaitForAssertion(() => Message(page).Should().Be("바코드 2건을 만들었습니다."));
        _confirm.Questions.Should().Equal("대상 품목 2건 중 1건은 바코드가 있어 덮어씁니다. 품목 코드 순으로 2001~2002을 넣을까요?");
        _api.Generated.Should().Equal(new GenerateItemBarcodesRequest("FR", "Y", "2000"));
        Rows(page).Select(r => r[3]).Should().Equal("2001", "2002");
    }

    [Fact, Trait("Id", "MM-UT-815")]
    public void Generate_Declined_DoesNotGenerate()
    {
        var page = RenderAs("admin");
        Search(page);
        _confirm.Answer = false;

        page.Find("[data-testid=item-barcode-search-generate]").Click();

        page.WaitForAssertion(() => _confirm.Questions.Should().ContainSingle());
        _api.Generated.Should().BeEmpty();
        Rows(page).Select(r => r[3]).Should().Equal("1000000005", "");
    }

    [Fact, Trait("Id", "BC-UC-011-T04"), Trait("Id", "MM-UT-817")]
    public void Generate_PreviewWithConflicts_ShowsMmE817WithTheOtherItems_WithoutAsking()
    {
        _api.Conflicts = [new ItemBarcodeConflictDto("A100", "2001", "Z900"), new ItemBarcodeConflictDto("B200", "2002", "X100")];
        var page = RenderAs("admin");
        Search(page);

        page.Find("[data-testid=item-barcode-search-generate]").Click();

        page.WaitForAssertion(() => Message(page).Should().Be("바코드가 다른 품목(X100, Z900)과 겹칩니다. 아무것도 저장하지 않았습니다."));
        _confirm.Questions.Should().BeEmpty();
        _api.Generated.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-011-T03"), Trait("Id", "MM-UT-816")]
    public void Generate_StartNoTheApiRejects_ShowsMmE816()
    {
        _api.PreviewProblem = """{"status":422,"code":"MM-E-816","args":["10"]}""";
        var page = RenderAs("admin");
        Search(page);

        page.Find("[data-testid=item-barcode-search-generate]").Click();

        page.WaitForAssertion(() => Message(page).Should().Be("시작 번호는 숫자여야 하고, 만들어지는 바코드는 10자리 이하여야 합니다."));
        _api.Generated.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public void UserWithoutFullAccess_CanSearch_ButCannotGenerate()
    {
        var page = RenderAs("user");

        Search(page);

        Rows(page).Should().HaveCount(2);
        Grid(page).IsReadOnly(0, "upcCode").Should().BeTrue();
        page.FindAll("[data-testid=item-barcode-search-generate]").Should().BeEmpty();
        page.Find("[data-testid=item-barcode-search-start-no]").HasAttribute("readonly").Should().BeTrue();
    }

    private IRenderedComponent<ItemBarcode> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<ItemBarcode>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=item-barcode-search-item-class] option").Should().HaveCountGreaterThan(1));
        return page;
    }

    private static void Search(IRenderedComponent<ItemBarcode> page)
    {
        page.Find("[data-testid=item-barcode-search-item-class]").Change("FR");
        page.Find("[data-testid=item-barcode-toolbar-search]").Click();
        page.WaitForAssertion(() => Grid(page).Rows.Should().HaveCount(2));
    }

    private static DataGridPage<ItemBarcodeDto> Grid(IRenderedComponent<ItemBarcode> page) =>
        page.DataGrid<ItemBarcodeDto, ItemBarcode>("item-barcode-grid");

    // The view-only grid's columns: item code, name, price and barcode
    private static List<string[]> Rows(IRenderedComponent<ItemBarcode> page)
    {
        var grid = Grid(page);
        return [.. Enumerable.Range(0, grid.Rows.Count)
            .Select(i => new[] { grid.Text(i, "itemCd"), grid.Text(i, "itemName"), grid.Text(i, "soUsdPrc"), grid.Text(i, "upcCode") })];
    }

    private static string? Value(IRenderedComponent<ItemBarcode> page, string testId) =>
        page.Find($"[data-testid={testId}]").GetAttribute("value");

    private static string Message(IRenderedComponent<ItemBarcode> page) =>
        page.Find("[data-testid=item-barcode-message]").TextContent;

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>Item barcode, code and localization API with two FR items; generate gives them start + 1, + 2.</summary>
    private sealed class FakeBarcodeApi : HttpMessageHandler
    {
        private List<ItemBarcodeDto> _items =
        [
            new(1, "A100", "APPLE", 4m, "1000000005"),
            new(2, "B200", "BOLT", 1.5m, null),
        ];

        public List<string> Requests { get; } = [];

        public List<GenerateItemBarcodesRequest> Generated { get; } = [];

        public List<ItemBarcodeConflictDto> Conflicts { get; set; } = [];

        public string? PreviewProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(request.RequestUri.PathAndQuery);
            if (path.StartsWith("/api/codes/", StringComparison.Ordinal))
            {
                return Json(path["/api/codes/".Length..] switch
                {
                    "ITEM_CLASS" => new CodeItemDto[] { new("FR", "과일"), new("HW", "철물") },
                    "DISPLAY_OPTIONS" => [new("Y", "사용"), new("N", "미사용"), new("ALL", "전체")],
                    _ => [],
                });
            }
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT", [],
                    [new CurrencyDto("USD", "US Dollar", 2, "US")],
                    [new CountryDecimalDto("US", "MM", 2), new CountryDecimalDto("US", "SD", 4)],
                    [new DecimalSettingDto("MM", 3, "HALF_UP")]));
            }
            if (path == "/" + ItemBarcodeRoutes.Base)
            {
                return Json(_items);
            }
            if (path == $"/{ItemBarcodeRoutes.Base}/{ItemBarcodeRoutes.Preview}")
            {
                if (PreviewProblem is not null)
                {
                    return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                    {
                        Content = new StringContent(PreviewProblem, null, "application/problem+json"),
                    };
                }
                var start = long.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["startNo"]!, CultureInfo.InvariantCulture);
                var items = _items.Select((i, n) => i with { UpcCode = (start + n + 1).ToString(CultureInfo.InvariantCulture) }).ToList();
                return Json(new ItemBarcodePreviewDto(items.Count, _items.Count(i => i.UpcCode is not null), items, Conflicts));
            }
            if (path == $"/{ItemBarcodeRoutes.Base}/{ItemBarcodeRoutes.Generate}")
            {
                var generate = (await request.Content!.ReadFromJsonAsync<GenerateItemBarcodesRequest>(cancellationToken))!;
                Generated.Add(generate);
                var start = long.Parse(generate.StartNo!, CultureInfo.InvariantCulture);
                _items = _items.Select((i, n) => i with { UpcCode = (start + n + 1).ToString(CultureInfo.InvariantCulture) }).ToList();
                return Json(new GenerateItemBarcodesResultDto(_items.Count));
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

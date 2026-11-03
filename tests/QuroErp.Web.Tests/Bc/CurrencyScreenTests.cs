using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Pages.Bc.Models;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Currency screen /bc/currency (BC-UC-026, TASK-BC-052) against a fake currency API.</summary>
public class CurrencyScreenTests : BunitContext
{
    private static readonly DateTimeOffset Read = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeCurrencyApi _api = new();
    private readonly BunitAuthorizationContext _auth;

    public CurrencyScreenTests()
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
        Services.AddHttpClient<CurrencyApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "BC-UT-411")]
    public void Open_ListsEveryCurrency_WithNamesPerLanguageInUse_EnFirst()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        grid.Headers.Select(h => h.Title).Should()
            .ContainInOrder("통화 코드", "이름 (English)", "이름 (한국어)", "금액 자릿수", "국가", "사용");
        grid.Column("currencyCode").Should().Equal("JPY", "USD");
        grid.Column("nameEn").Should().Equal("Yen", "US Dollar");
        grid.Column("nameKo").Should().Equal("", "미국 달러");
        grid.Column("decimals").Should().Equal("0", "2");
        grid.Column("countryCode").Should().Equal("JP", "US");
        new[] { grid.IsChecked(0, "useYn"), grid.IsChecked(1, "useYn") }.Should().Equal(false, true);
        _api.Searches.Should().Be(1);
    }

    [Fact]
    public void LanguageNotInUse_HasNoNameColumn()
    {
        _api.Languages = [new("en", 1)];

        var grid = Grid(RenderAs("admin"));

        grid.Headers.Select(h => h.Field).Should().Contain("nameEn").And.NotContain("nameKo");
    }

    [Fact, Trait("Id", "BC-UT-411")]
    public void CurrencyCode_IsLockedOnLoadedRows_AndOpenOnNewOnes()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        page.Find("[data-testid=currency-toolbar-new]").Click();

        grid.IsReadOnly(0, "currencyCode").Should().BeTrue();
        grid.IsReadOnly(1, "currencyCode").Should().BeTrue();
        grid.IsReadOnly(2, "currencyCode").Should().BeFalse();
        grid.Text(2, "decimals").Should().BeEmpty();
        grid.IsChecked(2, "useYn").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-428")]
    public void AddChangeAndDelete_AreSavedTogether_WithNamesInLanguagesInUseOnly()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);

        grid.Edit(0, "nameKo", "엔").Should().BeTrue();
        grid.Toggle(0, "useYn").Should().BeTrue();
        grid.Click(1, "deleteRow");
        page.Find("[data-testid=currency-toolbar-new]").Click();
        grid.Edit(2, "currencyCode", "eur").Should().BeTrue();
        grid.Edit(2, "nameEn", "Euro").Should().BeTrue();
        grid.Edit(2, "countryCode", "de").Should().BeTrue();

        page.Find("[data-testid=currency-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=currency-message]").TextContent.Should().Be("저장했습니다."));
        var saved = _api.Saved.Should().ContainSingle().Subject;
        saved.Added.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = (long?)null,
            CurrencyCode = "EUR",
            Decimals = (int?)null,
            CountryCode = "DE",
            UseYn = true,
            UpdatedAt = (DateTimeOffset?)null,
            Names = new Dictionary<string, string?> { ["en"] = "Euro", ["ko"] = null },
        });
        saved.Updated.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 2L,
            CurrencyCode = "JPY",
            Decimals = 0,
            CountryCode = "JP",
            UseYn = true,
            UpdatedAt = Read,
            Names = new Dictionary<string, string?> { ["en"] = "Yen", ["ko"] = "엔" },
        });
        saved.Deleted.Should().Equal(new CurrencyKey(1, Read));
    }

    [Fact, Trait("Id", "BC-UC-026-T04")]
    public void NewRow_WithoutTheEnName_IsNotSent()
    {
        var page = RenderAs("admin");
        var grid = Grid(page);
        page.Find("[data-testid=currency-toolbar-new]").Click();
        grid.Edit(2, "currencyCode", "EUR");

        page.Find("[data-testid=currency-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=currency-message]").TextContent.Should().Be("이름 (English)을(를) 입력하세요."));
        _api.Saved.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "BC-E-411", "통화 'USD'이(가) 올바르지 않습니다. 통화 코드는 영문 대문자 3자, 자릿수는 0~4입니다.")]
    [InlineData(HttpStatusCode.Conflict, "BC-E-411", "통화 'USD'이(가) 올바르지 않습니다. 통화 코드는 영문 대문자 3자, 자릿수는 0~4입니다.")]
    [InlineData(HttpStatusCode.Conflict, "BC-E-428",
        "통화 'USD'의 국가가 올바르지 않습니다. 국가 코드는 영문 대문자 2자이거나 비워 둡니다. 문서가 있는 통화의 국가는 바꿀 수 없습니다.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "BC-E-312", "기준 통화 USD은(는) 사용 중인 통화가 아니거나, 거래가 생긴 뒤라 바꿀 수 없습니다.")]
    [InlineData(HttpStatusCode.Conflict, "Common-E-004", "다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요.")]
    [Trait("Id", "BC-UC-026-T01"), Trait("Id", "BC-UC-026-T02"), Trait("Id", "BC-UC-026-T03")]
    public void RejectedSave_ShowsTheApiMessage_AndTheGridKeepsTheChanges(HttpStatusCode status, string code, string message)
    {
        _api.SaveProblem = (status, $$"""{"status":{{(int)status}},"code":"{{code}}","args":["USD"]}""");
        var page = RenderAs("admin");
        var grid = Grid(page);
        grid.Edit(1, "decimals", "3");

        page.Find("[data-testid=currency-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=currency-message]").TextContent.Should().Be(message));
        grid.Text(1, "decimals").Should().Be("3");
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Id", "CORE-051-WT-03")]
    public void UserWithoutFullAccess_CanOnlyRead_NewAndSaveAreDisabledNotHidden()
    {
        var page = RenderAs("user");
        var grid = Grid(page);

        page.Find("[data-testid=currency-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=currency-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=currency-toolbar-search]").HasAttribute("disabled").Should().BeFalse();
        page.Find("[data-testid=currency-toolbar-export]").HasAttribute("disabled").Should().BeFalse();
        grid.IsReadOnly(0, "nameEn").Should().BeTrue();

        // The handlers do nothing either
        grid.Edit(0, "nameEn", "Y").Should().BeFalse();
        page.Find("[data-testid=currency-toolbar-new]").Click();
        grid.Click(0, "deleteRow");
        page.Find("[data-testid=currency-toolbar-save]").Click();
        grid.Rows.Should().HaveCount(2);
        grid.Column("rowState").Should().OnlyContain(s => s.Length == 0);
        _api.Saved.Should().BeEmpty();
    }

    [Fact]
    public void EveryInputButtonAndGrid_HasTestIdByRule()
    {
        var page = RenderAs("admin");

        var elements = page.FindAll("input, select, textarea, button:not([hidden]), .quro-grid");
        elements.Should().NotBeEmpty();
        elements.Select(DataGridPageExtensions.TestIdOf).Should()
            .OnlyContain(id => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^currency(-(search|grid|toolbar|message)(-[a-z0-9]+)*)?$"));
    }

    [Theory, Trait("Id", "BC-UT-411")]
    [InlineData(null, null)]
    [InlineData(0.0, 0)]
    [InlineData(4.0, 4)]
    [InlineData(2.5, -1)]
    [InlineData(5.0, -1)]
    public void ToRequest_SendsWholeDecimals_AndOthersAsAValueTheApiRejects(double? decimals, int? sent)
    {
        var row = new CurrencyRow { CurrencyCode = "EUR", Decimals = (decimal?)decimals };

        var request = CurrencyRow.ToRequest(new BatchChanges<CurrencyRow>([row], [], []));

        request.Added.Should().ContainSingle().Which.Decimals.Should().Be(sent);
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    private IRenderedComponent<Currency> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Currency>();
        page.WaitForAssertion(() => Grid(page).Rows.Should().NotBeEmpty());
        return page;
    }

    private static DataGridPage<CurrencyRow> Grid(IRenderedComponent<Currency> page) => page.DataGrid<CurrencyRow, Currency>("currency-grid");

    /// <summary>Languages ko then en; JPY (Yen, not in use, with a name in ja, which is not in use) and USD (US Dollar/미국 달러, used by documents).</summary>
    private sealed class FakeCurrencyApi : HttpMessageHandler
    {
        private static readonly CurrencyMasterDto[] Currencies =
        [
            new(2, "JPY", "Yen", new Dictionary<string, string> { ["en"] = "Yen", ["ja"] = "円" }, 0, "JP", false, false, Read),
            new(1, "USD", "미국 달러", new Dictionary<string, string> { ["en"] = "US Dollar", ["ko"] = "미국 달러" }, 2, "US", true, true, Read),
        ];

        public int Searches { get; private set; }

        public IReadOnlyList<LanguageDto> Languages { get; set; } = [new("ko", 1), new("en", 2)];

        public List<BatchRequest<CurrencySaveRow, CurrencyKey>> Saved { get; } = [];

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT", Languages, [], [], []));
            }
            if (path == "/" + CurrencyRoutes.Batch && request.Method == HttpMethod.Post)
            {
                if (SaveProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                Saved.Add((await request.Content!.ReadFromJsonAsync<BatchRequest<CurrencySaveRow, CurrencyKey>>(cancellationToken))!);
                return Json<IReadOnlyList<CurrencyMasterDto>>(Currencies);
            }
            if (path != "/" + CurrencyRoutes.Base)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            Searches++;
            return Json<IReadOnlyList<CurrencyMasterDto>>(Currencies);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

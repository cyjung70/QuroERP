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

        page.FindAll("th").Select(h => h.TextContent.Trim()).Should()
            .ContainInOrder("통화 코드", "이름 (English)", "이름 (한국어)", "금액 자릿수", "국가", "사용");
        Values(page, "currency-grid-currency-code").Should().Equal("JPY", "USD");
        Values(page, "currency-grid-name-en").Should().Equal("Yen", "US Dollar");
        Values(page, "currency-grid-name-ko").Should().Equal("", "미국 달러");
        Values(page, "currency-grid-decimals").Should().Equal("0", "2");
        Values(page, "currency-grid-country-code").Should().Equal("JP", "US");
        page.FindAll("[data-testid=currency-grid-use-yn]").Select(c => c.HasAttribute("checked")).Should().Equal(false, true);
        _api.Searches.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-411")]
    public void CurrencyCode_IsLockedOnLoadedRows_AndOpenOnNewOnes_EnNameRequired()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=currency-toolbar-new]").Click();

        var codes = page.FindAll("[data-testid=currency-grid-currency-code]");
        codes.Take(2).Should().OnlyContain(c => c.HasAttribute("readonly"));
        codes[2].HasAttribute("readonly").Should().BeFalse();
        page.FindAll("[data-testid=currency-grid-name-en]")[2].GetAttribute("aria-required").Should().Be("true");
        page.FindAll("[data-testid=currency-grid-name-ko]")[2].HasAttribute("aria-required").Should().BeFalse();
        Values(page, "currency-grid-decimals")[2].Should().BeEmpty();
        page.FindAll("[data-testid=currency-grid-use-yn]")[2].HasAttribute("checked").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-428")]
    public void AddChangeAndDelete_AreSavedTogether_WithNamesInLanguagesInUseOnly()
    {
        var page = RenderAs("admin");

        page.FindAll("[data-testid=currency-grid-name-ko]")[0].Change("엔");
        page.FindAll("[data-testid=currency-grid-use-yn]")[0].Change(true);
        page.FindAll("[data-testid=currency-grid-delete-row]")[1].Click();
        page.Find("[data-testid=currency-toolbar-new]").Click();
        page.FindAll("[data-testid=currency-grid-currency-code]")[2].Change("eur");
        page.FindAll("[data-testid=currency-grid-name-en]")[2].Change("Euro");
        page.FindAll("[data-testid=currency-grid-country-code]")[2].Change("de");

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
        page.Find("[data-testid=currency-toolbar-new]").Click();
        page.FindAll("[data-testid=currency-grid-currency-code]")[2].Change("EUR");

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
        page.FindAll("[data-testid=currency-grid-decimals]")[1].Change("3");

        page.Find("[data-testid=currency-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=currency-message]").TextContent.Should().Be(message));
        Values(page, "currency-grid-decimals")[1].Should().Be("3");
    }

    [Fact, Trait("Id", "BC-UT-214"), Trait("Id", "CORE-051-WT-03")]
    public void UserWithoutFullAccess_CanOnlyRead_NewSaveAndRowButtonsAreDisabledNotHidden()
    {
        var page = RenderAs("user");
        var rows = page.FindAll("[data-testid=currency-grid] tbody tr").Count;

        page.Find("[data-testid=currency-toolbar-new]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=currency-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=currency-toolbar-search]").HasAttribute("disabled").Should().BeFalse();
        page.Find("[data-testid=currency-toolbar-export]").HasAttribute("disabled").Should().BeFalse();
        page.FindAll("[data-testid=currency-grid-delete-row]").Should().NotBeEmpty().And.OnlyContain(b => b.HasAttribute("disabled"));
        page.FindAll("[data-testid=currency-grid-name-en]").Should().OnlyContain(c => c.HasAttribute("readonly"));

        // The handlers do nothing either
        page.Find("[data-testid=currency-toolbar-new]").Click();
        page.FindAll("[data-testid=currency-grid-delete-row]")[0].Click();
        page.Find("[data-testid=currency-toolbar-save]").Click();
        page.FindAll("[data-testid=currency-grid] tbody tr").Should().HaveCount(rows);
        page.FindAll("[data-state=D]").Should().BeEmpty();
        _api.Saved.Should().BeEmpty();
    }

    [Fact]
    public void EveryInputButtonAndGrid_HasTestIdByRule()
    {
        var page = RenderAs("admin");

        var elements = page.FindAll("input, select, textarea, button:not([hidden]), .quro-grid");
        elements.Should().NotBeEmpty();
        elements.Select(e => e.GetAttribute("data-testid")).Should()
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
        page.WaitForAssertion(() => Values(page, "currency-grid-currency-code").Should().NotBeEmpty());
        return page;
    }

    private static List<string> Values(IRenderedComponent<Currency> page, string testId) =>
        page.FindAll($"[data-testid={testId}]").Select(e => e.GetAttribute("value") ?? "").ToList();

    /// <summary>Languages ko then en; JPY (Yen, not in use, with a name in ja, which is not in use) and USD (US Dollar/미국 달러, used by documents).</summary>
    private sealed class FakeCurrencyApi : HttpMessageHandler
    {
        private static readonly CurrencyMasterDto[] Currencies =
        [
            new(2, "JPY", "Yen", new Dictionary<string, string> { ["en"] = "Yen", ["ja"] = "円" }, 0, "JP", false, false, Read),
            new(1, "USD", "미국 달러", new Dictionary<string, string> { ["en"] = "US Dollar", ["ko"] = "미국 달러" }, 2, "US", true, true, Read),
        ];

        public int Searches { get; private set; }

        public List<BatchRequest<CurrencySaveRow, CurrencyKey>> Saved { get; } = [];

        public (HttpStatusCode Status, string Body)? SaveProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT", [new("ko", 1), new("en", 2)], [], [], []));
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

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Inventory;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Companies;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Inventory;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Company screen /bc/company (BC-UC-007, TASK-BC-014) against a fake company, settings and localization API.</summary>
public class CompanyScreenTests : BunitContext
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly FakeCompanyApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public CompanyScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<CompanyApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<AuthSettingApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<InventorySettingApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "BC-UT-301")]
    public void Open_LoadsTheCompany_WithNoNewOrDeleteButton()
    {
        var page = RenderAs("admin");

        Value(page, "company-header-name").Should().Be("Quro");
        Selected(page, "company-header-country-code").Should().Be("US");
        Selected(page, "company-header-base-currency-code").Should().Be("USD");
        page.Find("[data-testid=company-header-billing-mode-with_shipment]").HasAttribute("checked").Should().BeTrue();
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("company-toolbar-search", "company-toolbar-save");
    }

    [Fact, Trait("Id", "BC-UC-007-T01")]
    public void Save_PutsTheChangesWithUpdatedAt_AndKeepsTheNameEditable()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=company-header-name]").Change("Quro Inc");
        page.Find("[data-testid=company-header-business-type]").Change("제조");
        page.Find("[data-testid=company-header-billing-mode-separate]").Change("SEPARATE");

        page.Find("[data-testid=company-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-message]").TextContent.Should().Be("저장했습니다."));
        var request = _api.CompanyUpdates.Should().ContainSingle().Subject;
        request.Should().BeEquivalentTo(new
        {
            Name = "Quro Inc", BusinessType = "제조", BillingMode = "SEPARATE", CountryCode = "US", UpdatedAt = DateTimeOffset.UnixEpoch,
        });
        // BC-BR-302: the name is not a key
        page.Find("[data-testid=company-header-name]").HasAttribute("readonly").Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-UC-007-T02")]
    public void SaveWithoutName_ShowsRequiredMessage_AndDoesNotPut()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=company-header-name]").Change("");

        page.Find("[data-testid=company-toolbar-save]").Click();

        page.Find("[data-testid=company-message]").TextContent.Should().Be("회사 이름을(를) 입력하세요.");
        _api.CompanyUpdates.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-007-T03")]
    public void SaveAfterSomeoneElseSaved_Shows409Message()
    {
        var page = RenderAs("admin");
        _api.UpdateProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");

        page.Find("[data-testid=company-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UC-007-T04")]
    public void SaveWithAnUnknownCurrency_Shows422Message()
    {
        var page = RenderAs("admin");
        _api.UpdateProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"BC-E-312","args":["XYZ"]}""");

        page.Find("[data-testid=company-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-message]").TextContent.Should().Contain("XYZ"));
    }

    [Fact, Trait("Id", "BC-UT-303")]
    public void FieldError_ShowsTheFieldLabel()
    {
        var page = RenderAs("admin");
        _api.UpdateProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"BC-E-303","args":["Email"]}""");

        page.Find("[data-testid=company-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-message]").TextContent
            .Should().Be("이메일 값이 너무 길거나 형식이 올바르지 않습니다."));
    }

    [Fact, Trait("Id", "BC-UT-315")]
    public void KrCompany_NeedsTheRegisterNo()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=company-header-country-code]").Change("KR");

        page.Find("[data-testid=company-toolbar-save]").Click();

        page.Find("[data-testid=company-message]").TextContent.Should().Be("사업자등록번호을(를) 입력하세요.");
        _api.CompanyUpdates.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-305")]
    public void UserWithoutFullAccess_CanOnlyRead()
    {
        _api.HasLogo = true;
        var page = RenderAs("user");

        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid")).Should().Equal("company-toolbar-search");
        page.Find("[data-testid=company-header-name]").HasAttribute("readonly").Should().BeTrue();
        page.Find("[data-testid=company-header-country-code]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=company-security-lockout-minutes]").HasAttribute("readonly").Should().BeTrue();
        page.Find("[data-testid=company-security-auto-login-enabled]").HasAttribute("disabled").Should().BeTrue();
        page.FindAll("[data-testid=company-logo-delete], [data-testid=company-logo-upload], [data-testid=company-security-save], [data-testid=company-inventory-save]")
            .Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-306")]
    public void DeleteLogo_AfterConfirming_DeletesIt()
    {
        _api.HasLogo = true;
        var page = RenderAs("admin");
        page.WaitForElement("img.company-logo");

        page.Find("[data-testid=company-logo-delete]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-settings-message]").TextContent.Should().Be("삭제했습니다."));
        _confirm.Questions.Should().ContainSingle();
        _api.Requests.Should().Contain(r => r.Method == HttpMethod.Delete && r.RequestUri!.AbsolutePath == "/" + CompanyRoutes.Logo);
        page.FindAll("img.company-logo").Should().BeEmpty();
        page.Find("[data-testid=company-logo-delete]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-309")]
    public void SecuritySave_PutsTheValuesWithUpdatedAt()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=company-security-lockout-minutes]").Change("15");
        page.Find("[data-testid=company-security-password-reset-mode-email]").Change("EMAIL");

        page.Find("[data-testid=company-security-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-settings-message]").TextContent.Should().Be("저장했습니다."));
        _api.SecurityUpdates.Should().ContainSingle().Which.Should().Be(
            Security with { LockoutMinutes = 15, PasswordResetMode = "EMAIL" });
        _confirm.Questions.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-309")]
    public void SecurityValueOutOfRange_ShowsTheFieldLabel()
    {
        var page = RenderAs("admin");
        _api.UpdateProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"BC-E-309","args":["LockoutMinutes"]}""");

        page.Find("[data-testid=company-security-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-settings-message]").TextContent
            .Should().Be("잠금 시간 (분, 1~1440) 값이 허용 범위를 벗어났습니다."));
    }

    [Fact, Trait("Id", "BC-UT-309")]
    public void SecurityWithAnEmptyNumber_ShowsRequiredMessage_AndDoesNotPut()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=company-security-password-min-length]").Change("");

        page.Find("[data-testid=company-security-save]").Click();

        page.Find("[data-testid=company-settings-message]").TextContent.Should().Be("비밀번호 최소 길이 (8~128)을(를) 입력하세요.");
        _api.SecurityUpdates.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-310")]
    public void TurningAutoLoginOff_AsksFirst()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=company-security-auto-login-enabled]").Change(false);
        _confirm.Answer = false;

        page.Find("[data-testid=company-security-save]").Click();

        _confirm.Questions.Should().ContainSingle().Which.Should().Contain("자동 로그인");
        _api.SecurityUpdates.Should().BeEmpty();

        _confirm.Answer = true;
        page.Find("[data-testid=company-security-save]").Click();

        page.WaitForAssertion(() => _api.SecurityUpdates.Should().ContainSingle().Which.AutoLoginEnabled.Should().BeFalse());
    }

    private static readonly AuthSettingDto Security = new(5, 30, true, true, 30, 60, true, 8, 2, 0, 0, "ADMIN_ONLY", DateTimeOffset.UnixEpoch);

    private IRenderedComponent<Company> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<Company>();
        page.WaitForAssertion(() => Value(page, "company-header-name").Should().Be("Quro"));
        page.WaitForElement("[data-testid=company-security-lockout-minutes]");
        return page;
    }

    private static string? Value(IRenderedComponent<Company> page, string testId) =>
        page.Find($"[data-testid={testId}]").GetAttribute("value");

    private static string? Selected(IRenderedComponent<Company> page, string testId) =>
        page.Find($"[data-testid={testId}] option[selected]").GetAttribute("value");

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>A US company "Quro" with its settings; every update answers with the stored record or the set problem.</summary>
    private sealed class FakeCompanyApi : HttpMessageHandler
    {
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<UpdateCompanyRequest> CompanyUpdates { get; } = [];

        public List<AuthSettingDto> SecurityUpdates { get; } = [];

        public (HttpStatusCode Status, string Body)? UpdateProblem { get; set; }

        public bool HasLogo { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && UpdateProblem is { } problem)
            {
                return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
            }
            switch (path)
            {
                case "/" + LocalizationRoutes.Base:
                    return Json(new LocalizationDto("US", "USD", "en", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT",
                        [new LanguageDto("ko", 1), new LanguageDto("en", 2)],
                        [new CurrencyDto("USD", "미국 달러", 2, "US"), new CurrencyDto("KRW", "원", 0, "KR")], [], []));
                case "/" + CompanyRoutes.Base when request.Method == HttpMethod.Put:
                    var update = (await request.Content!.ReadFromJsonAsync<UpdateCompanyRequest>(Web, cancellationToken))!;
                    CompanyUpdates.Add(update);
                    return Json(Company(update.Name!) with { BillingMode = update.BillingMode!, UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1) });
                case "/" + CompanyRoutes.Base:
                    return Json(Company("Quro"));
                case "/" + CompanyRoutes.Logo when request.Method == HttpMethod.Delete:
                    HasLogo = false;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/" + CompanyRoutes.Logo when HasLogo:
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Png) { Headers = { ContentType = new("image/png") } },
                    };
                case "/" + CompanyRoutes.Logo:
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("""{"status":404,"code":"BC-E-007"}""", null, "application/problem+json"),
                    };
                case "/" + InventorySettingRoutes.Base:
                    return Json(new InventorySettingDto(NegativeStockModes.Deny, DateTimeOffset.UnixEpoch, "system"));
                case "/" + AuthSettingRoutes.Base when request.Method == HttpMethod.Put:
                    var security = (await request.Content!.ReadFromJsonAsync<AuthSettingDto>(Web, cancellationToken))!;
                    SecurityUpdates.Add(security);
                    return Json(security with { UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1) });
                case "/" + AuthSettingRoutes.Base:
                    return Json(Security);
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private CompanyDto Company(string name) =>
            new(name, null, null, null, null, null, null, null, null, null, null, "US", "USD", "en", "WITH_SHIPMENT", HasLogo, DateTimeOffset.UnixEpoch);

        private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: Web) };
    }
}

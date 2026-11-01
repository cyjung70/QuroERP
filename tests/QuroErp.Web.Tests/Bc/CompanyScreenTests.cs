using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.BusinessPlaces;
using QuroErp.Contracts.Bc.Companies;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Inventory;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Companies;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Pages.Bc.Models;
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
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<CompanyApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<BusinessPlaceApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
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
        Selected(page, "company-header-country-code").Should().BeEquivalentTo("US");
        Selected(page, "company-header-base-currency-code").Should().BeEquivalentTo("USD");
        page.Find("[data-testid=company-header-billing-mode-with_shipment]").HasAttribute("checked").Should().BeTrue();
        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("company-toolbar-search", "company-toolbar-save");
    }

    [Fact, Trait("Id", "CORE-050-WT-03")]
    public void Open_ShowsTheFourAuditValuesOfTheCompany()
    {
        var page = RenderAs("admin");

        page.WaitForAssertion(() => page.FindAll("[data-testid=company-audit] dd").Select(d => d.TextContent)
            .Should().Equal(AuditTimes.Shown("setup", "kim")));
    }

    [Fact, Trait("Id", "BC-UC-007-T01")]
    public void Save_PutsTheChangesWithUpdatedAt_AndKeepsTheNameEditable()
    {
        var page = RenderAs("admin");
        page.Field("company-header-name").Change("Quro Inc");
        page.Field("company-header-represent").Change("김");
        page.Field("company-header-billing-mode-separate").Change("SEPARATE");

        page.Find("[data-testid=company-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-message]").TextContent.Should().Be("저장했습니다."));
        var request = _api.CompanyUpdates.Should().ContainSingle().Subject;
        request.Should().BeEquivalentTo(new
        {
            Name = "Quro Inc",
            Represent = "김",
            BillingMode = "SEPARATE",
            CountryCode = "US",
            UpdatedAt = AuditTimes.Updated,
        });
        // BC-BR-302: the name is not a key
        page.Field("company-header-name").IsReadOnly.Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-024-WT-02"), Trait("Rule", "BC-BR-318")]
    public void TimeZone_IsADropdownOfIanaNames_AndIsSaved()
    {
        var page = RenderAs("admin");
        Selected(page, "company-header-time-zone").Should().BeEquivalentTo("America/New_York");
        // Combo codes come back in lower case (FormFieldPage)
        var options = page.Field("company-header-time-zone").Options.Select(o => o.Code).ToList();
        options.Should().Contain(["america/new_york", "asia/seoul"]).And.NotContain("korea standard time");

        page.Field("company-header-time-zone").Change("Asia/Seoul");
        page.Find("[data-testid=company-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-message]").TextContent.Should().Be("저장했습니다."));
        _api.CompanyUpdates.Should().ContainSingle().Which.TimeZone.Should().Be("Asia/Seoul");
    }

    [Fact, Trait("Id", "BC-UC-007-T02")]
    public void SaveWithoutName_ShowsRequiredMessage_AndDoesNotPut()
    {
        var page = RenderAs("admin");
        page.Field("company-header-name").Change("");

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

    [Fact, Trait("Id", "BC-UT-315"), Trait("Rule", "BC-BR-315")]
    public void CompanyForm_HasNoSupplierFields_TheBusinessPlaceAreaHasThem()
    {
        var page = RenderAs("admin");

        page.FindAll("[data-testid^=company-header-register-no], [data-testid^=company-header-business-]").Should().BeEmpty();
        Value(page, "company-business-place-business-place-code").Should().Be("MAIN");
        page.Field("company-business-place-business-place-code").IsReadOnly.Should().BeTrue();
        Value(page, "company-business-place-business-reg-no").Should().Be("1234567890");
    }

    [Fact, Trait("Id", "CORE-046-WT-01"), Trait("Rule", "BC-BR-320")]
    public void BusinessPlace_SavesOnItsOwn_WithUpdatedAt()
    {
        var page = RenderAs("admin");
        page.Field("company-business-place-ceo-name").Change("김대표");
        page.Field("company-business-place-business-item").Change("소프트웨어");

        page.Find("[data-testid=company-business-place-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-settings-message]").TextContent.Should().Be("저장했습니다."));
        _api.PlaceUpdates.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            BusinessPlaceName = "Quro",
            CeoName = "김대표",
            BusinessItem = "소프트웨어",
            UseYn = true,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        });
        _api.CompanyUpdates.Should().BeEmpty("the toolbar save is the company's only");
        Form(page).IsDirty.Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-046-WT-02"), Trait("Rule", "BC-BR-320")]
    public void KrBusinessPlace_NeedsTheRegistrationNumber()
    {
        _api.CountryCode = "KR";
        var page = RenderAs("admin");
        page.Field("company-business-place-business-reg-no").Change("");

        page.Find("[data-testid=company-business-place-save]").Click();

        page.Find("[data-testid=company-settings-message]").TextContent.Should().Be("사업자등록번호을(를) 입력하세요.");
        _api.PlaceUpdates.Should().BeEmpty();
    }

    [Fact, Trait("Id", "CORE-046-WT-03"), Trait("Rule", "BC-BR-320")]
    public void BusinessPlaceFieldError_ShowsTheFieldLabel()
    {
        var page = RenderAs("admin");
        _api.UpdateProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"BC-E-320","args":["SubBusinessNo"]}""");

        page.Find("[data-testid=company-business-place-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-settings-message]").TextContent.Should().StartWith("종사업장번호 값을 확인하세요."));
    }

    [Fact, Trait("Id", "BC-UT-317")]
    public void UnsavedBusinessPlaceChanges_GuardLeavingTheScreen()
    {
        var page = RenderAs("admin");

        page.Field("company-business-place-tel-no").Change("02-111-1111");

        Form(page).IsDirty.Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-305")]
    public void UserWithoutFullAccess_CanOnlyRead()
    {
        _api.HasLogo = true;
        var page = RenderAs("user");

        // Blocked buttons stay on the bar, disabled (screen-templates.md 0)
        page.FindAll("[role=toolbar] button:not([disabled])").Select(b => b.GetAttribute("data-testid")).Should().Equal("company-toolbar-search");
        page.Find("[data-testid=company-toolbar-save]").HasAttribute("disabled").Should().BeTrue();
        page.Field("company-header-name").IsReadOnly.Should().BeTrue();
        page.Field("company-header-country-code").IsReadOnly.Should().BeTrue();
        page.Find("[data-testid=company-security-lockout-minutes]").HasAttribute("readonly").Should().BeTrue();
        page.Find("[data-testid=company-security-auto-login-enabled]").HasAttribute("disabled").Should().BeTrue();
        page.FindAll("[data-testid=company-logo-delete], [data-testid=company-logo-upload], [data-testid=company-security-save], [data-testid=company-inventory-save], [data-testid=company-business-place-save]")
            .Should().BeEmpty();
        page.Field("company-business-place-business-place-name").IsReadOnly.Should().BeTrue();
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

    [Fact, Trait("Id", "BC-UT-317")]
    public void ToolbarSave_WithAnUnsavedSection_AsksFirst_AndSavesTheCompanyOnly()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=company-security-lockout-minutes]").Change("15");
        _confirm.Answer = false;

        page.Find("[data-testid=company-toolbar-save]").Click();

        _confirm.Questions.Should().ContainSingle().Which.Should().Contain("다른 영역");
        _api.CompanyUpdates.Should().BeEmpty();

        _confirm.Answer = true;
        page.Find("[data-testid=company-toolbar-save]").Click();

        page.WaitForAssertion(() => _api.CompanyUpdates.Should().ContainSingle());
        _api.SecurityUpdates.Should().BeEmpty();
        Value(page, "company-security-lockout-minutes").Should().Be("15");
        Form(page).IsDirty.Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-317")]
    public void Search_WithAnUnsavedSection_AsksFirst_AndReadsTheSectionsAgain()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=company-inventory-mode-allow]").Change(NegativeStockModes.Allow);
        _confirm.Answer = false;

        page.Find("[data-testid=company-toolbar-search]").Click();

        _confirm.Questions.Should().ContainSingle().Which.Should().StartWith("저장하지 않은 변경 내용이 있습니다.");
        page.Find("[data-testid=company-inventory-mode-allow]").HasAttribute("checked").Should().BeTrue();

        _confirm.Answer = true;
        page.Find("[data-testid=company-toolbar-search]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=company-inventory-mode-deny]").HasAttribute("checked").Should().BeTrue());
        Form(page).IsDirty.Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-UT-317")]
    public void UnsavedSectionChanges_GuardLeavingTheScreen_UntilSaved()
    {
        var page = RenderAs("admin");
        Form(page).IsDirty.Should().BeFalse();

        page.Find("[data-testid=company-security-allow-concurrent-login]").Change(false);

        Form(page).IsDirty.Should().BeTrue();

        page.Find("[data-testid=company-security-save]").Click();

        page.WaitForAssertion(() => Form(page).IsDirty.Should().BeFalse());
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

    private static QuroSingleForm<CompanyModel> Form(IRenderedComponent<Company> page) =>
        page.FindComponent<QuroSingleForm<CompanyModel>>().Instance;

    private static string? Value(IRenderedComponent<Company> page, string testId) => page.Field(testId).Value;

    private static string? Selected(IRenderedComponent<Company> page, string testId) => page.Field(testId).Selected;

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>A US company "Quro" with its settings; every update answers with the stored record or the set problem.</summary>
    private sealed class FakeCompanyApi : HttpMessageHandler
    {
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<UpdateCompanyRequest> CompanyUpdates { get; } = [];

        public List<AuthSettingDto> SecurityUpdates { get; } = [];

        public List<UpdateBusinessPlaceRequest> PlaceUpdates { get; } = [];

        public string CountryCode { get; set; } = "US";

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
                    return Json(new LocalizationDto("US", "USD", "en", "America/New_York", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT",
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
                case "/" + BusinessPlaceRoutes.Base:
                    return Json(new[] { Place });
                case "/" + BusinessPlaceRoutes.Base + "/1" when request.Method == HttpMethod.Put:
                    var place = (await request.Content!.ReadFromJsonAsync<UpdateBusinessPlaceRequest>(Web, cancellationToken))!;
                    PlaceUpdates.Add(place);
                    return Json(Place with { CeoName = place.CeoName, UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1) });
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
            new(name, null, null, null, null, null, null, null, CountryCode, "USD", "en", "America/New_York", "WITH_SHIPMENT", HasLogo,
                AuditTimes.Created, "setup", AuditTimes.Updated, "kim");

        private static readonly BusinessPlaceDto Place = new(
            1, "MAIN", "Quro", true, "1234567890", null, null, null, null, null, null, null, null, null, null, true,
            DateTimeOffset.UnixEpoch, "system", DateTimeOffset.UnixEpoch, "system");

        private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: Web) };
    }
}

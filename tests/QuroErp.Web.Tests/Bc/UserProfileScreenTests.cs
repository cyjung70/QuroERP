using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.UserProfiles;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Mm.Warehouses;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>My info screen /bc/user-profile (BC-UC-003, TASK-BC-006) against a fake my info, warehouse and localization API.</summary>
public class UserProfileScreenTests : BunitContext
{
    private static readonly DateTimeOffset Loaded = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeProfileApi _api = new();
    private readonly FakeConfirm _confirm = new();

    public UserProfileScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(Configure);
        Services.AddHttpClient<UserProfileApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<WarehouseApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);

        var auth = AddAuthorization();
        var sessionId = Services.GetRequiredService<AccessTokenStore>()
            .Add(new LoginResponse("token-kim", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "user", false, "ko"));
        auth.SetAuthorized("kim");
        auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
    }

    [Fact, Trait("Id", "BC-UT-101")]
    public void Load_ShowsMyInfo_UserIdLocked_AndTheCompanyDefaultWarehouse()
    {
        var page = Open();

        page.Find("[data-testid=user-profile-header-userid]").GetAttribute("value").Should().Be("kim");
        page.Find("[data-testid=user-profile-header-userid]").HasAttribute("readonly").Should().BeTrue();
        page.Find("[data-testid=user-profile-header-username]").GetAttribute("value").Should().Be("Kim");
        page.FindAll("[data-testid=user-profile-header-lang-code] option").Select(o => o.TextContent).Should().Contain(["한국어", "English"]);
        page.FindAll("[data-testid=user-profile-header-default-warehouse-id] option").Select(o => o.TextContent)
            .Should().Contain(["MAIN Main", "W02 Second"]);
        page.Find("[data-testid=user-profile-header-company-warehouse]").TextContent.Should().Contain("MAIN Main");
    }

    [Fact, Trait("Id", "BC-UT-103")]
    public void Screen_HasNoPasswordField_AndLinksToPasswordChange()
    {
        var page = Open();

        page.FindAll("input[type=password]").Should().BeEmpty();
        page.Find("[data-testid=user-profile-change-password]").GetAttribute("href").Should().Be(WebAuthentication.ChangePasswordPath);
    }

    [Fact, Trait("Id", "BC-UT-104")]
    public void Toolbar_HasSearchAndSave_ButNoNewOrDelete()
    {
        var page = Open();

        page.FindAll("[data-testid=user-profile-toolbar-save]").Should().ContainSingle();
        page.FindAll("[data-testid=user-profile-toolbar-new]").Should().BeEmpty();
        page.FindAll("[data-testid=user-profile-toolbar-delete]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-003-T01")]
    public void Save_SendsTheFieldsWithUpdatedAt_AndShowsSaved()
    {
        var page = Open();
        page.Find("[data-testid=user-profile-header-username]").Change("Kim Min");
        page.Find("[data-testid=user-profile-header-email]").Change("kim@example.com");
        page.Find("[data-testid=user-profile-header-default-warehouse-id]").Change("2");

        page.Find("[data-testid=user-profile-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-profile-message]").TextContent.Should().Be("저장했습니다."));
        _api.Updates.Should().ContainSingle().Which.Should().Be(
            new UpdateUserProfileRequest("Kim Min", "kim@example.com", null, "ko", 2, Loaded));
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "quroSubmit");
    }

    [Fact, Trait("Id", "BC-UC-003-T02")]
    public void Save_WithoutName_ShowsRequired_AndSendsNothing()
    {
        var page = Open();
        page.Find("[data-testid=user-profile-header-username]").Change("");

        page.Find("[data-testid=user-profile-toolbar-save]").Click();

        page.Find("[data-testid=user-profile-message]").TextContent.Should().Contain("이름");
        _api.Updates.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-003-T03")]
    public void Save_AfterSomeoneElse_ShowsCommonE004()
    {
        _api.UpdateProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");
        var page = Open();

        page.Find("[data-testid=user-profile-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-profile-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UT-102")]
    public void Save_FieldError_ShowsTheFieldLabel()
    {
        _api.UpdateProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"BC-E-102","args":["Email"]}""");
        var page = Open();

        page.Find("[data-testid=user-profile-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-profile-message]").TextContent.Should().StartWith("이메일 값이"));
    }

    [Fact, Trait("Id", "BC-UT-403")]
    public void Save_LanguageNotInUse_SaysSo()
    {
        _api.UpdateProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"BC-E-405","args":["sw"]}""");
        var page = Open();

        page.Find("[data-testid=user-profile-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-profile-message]").TextContent
            .Should().Be("언어 'sw'은(는) 사용 중인 언어가 아닙니다."));
    }

    [Fact, Trait("Id", "BC-UT-105")]
    public void Save_WithANewLanguage_SubmitsTheCultureForm()
    {
        var page = Open();
        page.Find("[data-testid=user-profile-header-lang-code]").Change("en");

        page.Find("[data-testid=user-profile-toolbar-save]").Click();

        page.WaitForAssertion(() => JSInterop.Invocations.Should().Contain(i => i.Identifier == "quroSubmit"));
        page.Find($"form[action='{WebLocalization.CulturePath}'] input[name=culture]").GetAttribute("value").Should().Be("en");
    }

    [Fact, Trait("Id", "BC-UT-106")]
    public void RevokeTokens_AsksFirst_ThenPosts()
    {
        var page = Open();

        _confirm.Answer = false;
        page.Find("[data-testid=user-profile-revoke-tokens]").Click();
        _api.Revokes.Should().Be(0);

        _confirm.Answer = true;
        page.Find("[data-testid=user-profile-revoke-tokens]").Click();
        page.WaitForAssertion(() => page.Find("[data-testid=user-profile-message]").TextContent.Should().Be("모든 기기의 자동 로그인을 끊었습니다."));
        _api.Revokes.Should().Be(1);
    }

    private IRenderedComponent<UserProfile> Open()
    {
        var page = Render<UserProfile>();
        page.WaitForElement("[data-testid=user-profile-header-username]");
        return page;
    }

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>My info of kim (lang ko, company default warehouse), warehouses MAIN and W02, languages ko and en.</summary>
    private sealed class FakeProfileApi : HttpMessageHandler
    {
        private UserProfileDto _profile = new("kim", "Kim", null, null, "ko", null, "MAIN", "Main", Loaded);

        public List<UpdateUserProfileRequest> Updates { get; } = [];

        public int Revokes { get; private set; }

        public (HttpStatusCode Status, string Body)? UpdateProblem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("KR", "KRW", "ko", "VAT", "yyyy-MM-dd", "DOCUMENT",
                    [new LanguageDto("ko", 1), new LanguageDto("en", 2)], [], [], []));
            }
            if (path == "/" + WarehouseRoutes.Options)
            {
                return Json(new[] { new WarehouseOptionDto(1, "MAIN", "Main"), new WarehouseOptionDto(2, "W02", "Second") });
            }
            if (path == "/" + UserProfileRoutes.RevokeTokens && request.Method == HttpMethod.Post)
            {
                Revokes++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path == "/" + UserProfileRoutes.Base && request.Method == HttpMethod.Get)
            {
                return Json(_profile);
            }
            if (path == "/" + UserProfileRoutes.Base && request.Method == HttpMethod.Put)
            {
                if (UpdateProblem is { } problem)
                {
                    return new HttpResponseMessage(problem.Status) { Content = new StringContent(problem.Body, null, "application/problem+json") };
                }
                var update = (await request.Content!.ReadFromJsonAsync<UpdateUserProfileRequest>(cancellationToken))!;
                Updates.Add(update);
                _profile = _profile with
                {
                    UserName = update.UserName, Email = update.Email, Phone = update.Phone, LangCode = update.LangCode,
                    DefaultWarehouseId = update.DefaultWarehouseId, UpdatedAt = Loaded.AddSeconds(1),
                };
                return Json(_profile);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

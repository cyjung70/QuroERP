using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Password change screen /bc/change-password (BC-UC-002, TASK-BC-004) against a fake auth API.</summary>
public class ChangePasswordScreenTests : BunitContext
{
    private readonly FakeAuthApi _api = new();
    private readonly AccessTokenStore _store;
    private readonly BunitAuthorizationContext _auth;

    public ChangePasswordScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddCompanyTime();
        Services.AddSingleton<IQuroConfirm>(new FakeConfirm());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"))
            .ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
        _store = Services.GetRequiredService<AccessTokenStore>();
    }

    [Fact, Trait("Id", "BC-004-WT-01")]
    public void ForcedChange_ShowsAuth005_AndSuccessReplacesTheTokenAndGoesHome()
    {
        var (page, sessionId) = RenderAs(mustChange: true);
        page.Find("[data-testid=change-password-message]").TextContent.Should().Be("계속하려면 비밀번호를 변경하세요.");

        Fill(page, "Current-Pass-1", "Spring-2026", "Spring-2026");
        page.Find("[data-testid=change-password-toolbar-save]").Click();

        page.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/"));
        _store.Get(sessionId)!.Should().BeEquivalentTo(new { AccessToken = "token-new", MustChangePassword = false });
        _api.Requests.Should().ContainSingle().Which.Should().Be(("token-old", new ChangePasswordRequest("Current-Pass-1", "Spring-2026")));
    }

    [Fact, Trait("Id", "BC-UC-002-T01")]
    public void VoluntaryChange_ShowsChangedMessage_AndClearsTheInputs()
    {
        var (page, _) = RenderAs(mustChange: false);
        page.FindAll("[data-testid=change-password-message]").Should().BeEmpty();

        Fill(page, "Current-Pass-1", "Spring-2026", "Spring-2026");
        page.Find("[data-testid=change-password-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=change-password-message]").TextContent.Should().Be("비밀번호를 변경했습니다."));
        page.Find("[data-testid=change-password-header-new-password]").GetAttribute("value").Should().BeNullOrEmpty();
        page.Find("[data-testid=change-password-header-new-password]").GetAttribute("type").Should().Be("password");
    }

    [Fact, Trait("Id", "BC-UT-057")]
    public void ConfirmationDiffers_ShowsBcE057UnderTheInput_AndDoesNotCallTheApi()
    {
        var (page, _) = RenderAs(mustChange: false);

        Fill(page, "Current-Pass-1", "Spring-2026", "Spring-2027");
        page.Find("[data-testid=change-password-toolbar-save]").Click();

        page.Find("[data-testid=change-password-header-confirm-password-error]").TextContent.Should().Be("새 비밀번호와 확인 값이 같지 않습니다.");
        _api.Requests.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-002-T02")]
    public void EmptyRequiredInput_ShowsRequiredMessage_AndDoesNotCallTheApi()
    {
        var (page, _) = RenderAs(mustChange: false);

        Fill(page, null, "Spring-2026", "Spring-2026");
        page.Find("[data-testid=change-password-toolbar-save]").Click();

        page.Find("[data-testid=change-password-message]").TextContent.Should().Be("현재 비밀번호을(를) 입력하세요.");
        _api.Requests.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-059")]
    public void ApiRuleViolation_ShowsTheAuthMessage_AndKeepsTheOldToken()
    {
        var (page, sessionId) = RenderAs(mustChange: true);
        _api.Problem = """{"status":400,"code":"AUTH-012","args":[3]}""";

        Fill(page, "Current-Pass-1", "abcdefgh1", "abcdefgh1");
        page.Find("[data-testid=change-password-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=change-password-message]").TextContent
            .Should().Be("영문 대문자, 소문자, 숫자, 특수문자 중 3가지 이상을 쓰세요."));
        _store.Get(sessionId)!.AccessToken.Should().Be("token-old");
    }

    private (IRenderedComponent<ChangePassword> Page, string SessionId) RenderAs(bool mustChange)
    {
        var sessionId = _store.Add(new LoginResponse("token-old", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "user", mustChange, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        return (Render<ChangePassword>(), sessionId);
    }

    private static void Fill(IRenderedComponent<ChangePassword> page, string? current, string next, string confirm)
    {
        if (current is not null)
        {
            page.Find("[data-testid=change-password-header-current-password]").Change(current);
        }
        page.Find("[data-testid=change-password-header-new-password]").Change(next);
        page.Find("[data-testid=change-password-header-confirm-password]").Change(confirm);
    }

    /// <summary>POST api/auth/change-password: records (token, request) and answers a new token or <see cref="Problem"/>.</summary>
    private sealed class FakeAuthApi : HttpMessageHandler
    {
        public List<(string? Token, ChangePasswordRequest Request)> Requests { get; } = [];

        public string? Problem { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/" + AuthRoutes.ChangePassword)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            Requests.Add((request.Headers.Authorization?.Parameter,
                (await request.Content!.ReadFromJsonAsync<ChangePasswordRequest>(cancellationToken))!));
            return Problem is not null
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(Problem, null, "application/problem+json") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new LoginResponse("token-new", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "user", false, "ko")),
                };
        }
    }
}

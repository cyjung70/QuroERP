using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Bc.Localization;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Contracts.Bc.Users;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Bc.Roles;
using QuroErp.Web.Bc.Users;
using QuroErp.Web.Common;
using QuroErp.Web.Components.Pages.Bc;
using QuroErp.Web.Components.Screens;
using QuroErp.Web.Tests.Screens;

namespace QuroErp.Web.Tests.Bc;

/// <summary>User management screen /bc/user (BC-UC-004, TASK-BC-008) against a fake user, role, code and localization API.</summary>
public class UserScreenTests : BunitContext
{
    private readonly FakeUserApi _api = new();
    private readonly FakeConfirm _confirm = new();
    private readonly BunitAuthorizationContext _auth;

    public UserScreenTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ko");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWebLocalization();
        Services.AddSingleton<IQuroConfirm>(_confirm);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<AccessTokenStore>();
        Services.AddScoped<AccessTokenProvider>();
        Services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri("http://api.test"));
        Services.AddHttpClient<UserApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<RoleApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<CodeApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        Services.AddHttpClient<LocalizationApiClient>(Configure).ConfigurePrimaryHttpMessageHandler(() => _api);
        _auth = AddAuthorization();
    }

    [Fact, Trait("Id", "BC-008-WT-01")]
    public void New_ShowsPasswordFields_InUseDefault_AndNoEndDate()
    {
        var page = RenderAs("admin");

        page.FindAll("[data-testid=user-header-password]").Should().ContainSingle();
        page.FindAll("[data-testid=user-header-confirm-password]").Should().ContainSingle();
        page.Find("[data-testid=user-header-valid-flag-y]").HasAttribute("checked").Should().BeTrue();
        Value(page, "user-header-user-valid-dt").Should().BeNullOrEmpty();
        page.Find("[data-testid=user-header-role-id] option[value='2']").TextContent.Should().Be("user");
        page.FindAll("[data-testid=user-header-lang-code] option").Select(o => o.GetAttribute("value")).Should().Contain(["ko", "en"]);
        page.FindAll("[data-testid=user-actions]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-004-T01")]
    public void SaveNew_PostsALowerCaseUserIdWithThePassword_AndLocksTheUserId()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=user-header-userid]").Change("New.User");
        page.Find("[data-testid=user-header-username]").Change("New User");
        page.Find("[data-testid=user-header-password]").Change("Initial-Pass-1");
        page.Find("[data-testid=user-header-confirm-password]").Change("Initial-Pass-1");
        page.Find("[data-testid=user-header-role-id]").Change("2");

        page.Find("[data-testid=user-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().Be("저장했습니다."));
        var created = _api.Created.Should().ContainSingle().Subject;
        created.Should().BeEquivalentTo(new { UserId = "new.user", Password = "Initial-Pass-1", RoleId = 2L, ValidFlag = "Y", UserValidDt = (DateOnly?)null });
        page.Find("[data-testid=user-header-userid]").HasAttribute("readonly").Should().BeTrue();
        page.FindAll("[data-testid=user-header-password]").Should().BeEmpty();
        Value(page, "user-search-userid").Should().Be("new.user");
    }

    [Fact, Trait("Id", "BC-UC-004-T02")]
    public void SaveWithoutRole_ShowsRequiredMessage_AndDoesNotPost()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=user-header-userid]").Change("new.user");
        page.Find("[data-testid=user-header-username]").Change("New User");
        page.Find("[data-testid=user-header-password]").Change("Initial-Pass-1");
        page.Find("[data-testid=user-header-confirm-password]").Change("Initial-Pass-1");

        page.Find("[data-testid=user-toolbar-save]").Click();

        page.Find("[data-testid=user-message]").TextContent.Should().Be("역할을(를) 입력하세요.");
        _api.Created.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-123")]
    public void SaveNew_WithDifferentPasswords_SaysSo_AndDoesNotPost()
    {
        var page = RenderAs("admin");
        page.Find("[data-testid=user-header-userid]").Change("new.user");
        page.Find("[data-testid=user-header-username]").Change("New User");
        page.Find("[data-testid=user-header-password]").Change("Initial-Pass-1");
        page.Find("[data-testid=user-header-confirm-password]").Change("Initial-Pass-2");
        page.Find("[data-testid=user-header-role-id]").Change("2");

        page.Find("[data-testid=user-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().Be("새 비밀번호와 확인 값이 같지 않습니다."));
        _api.Created.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-004-T03")]
    public void UpdateAfterSomeoneElseSaved_Shows409Message()
    {
        var page = RenderAs("admin");
        Search(page, "lee");
        _api.UpdateProblem = (HttpStatusCode.Conflict, """{"status":409,"code":"Common-E-004"}""");
        page.Find("[data-testid=user-header-username]").Change("Lee 2");

        page.Find("[data-testid=user-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent
            .Should().Be("다른 사용자가 먼저 수정했습니다. 다시 조회한 뒤 저장하세요."));
    }

    [Fact, Trait("Id", "BC-UC-004-T04")]
    public void SaveWithAnUnknownCode_Shows422Message()
    {
        var page = RenderAs("admin");
        Search(page, "lee");
        _api.UpdateProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"Common-E-002","args":["VALID_FLAG","X"]}""");

        page.Find("[data-testid=user-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().Contain("'X'"));
    }

    [Fact, Trait("Id", "BC-UT-122")]
    public void InvalidField_IsShownWithItsLabel()
    {
        var page = RenderAs("admin");
        Search(page, "lee");
        _api.UpdateProblem = (HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"BC-E-122","args":["Email"]}""");

        page.Find("[data-testid=user-toolbar-save]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().StartWith("이메일 값이 올바르지 않습니다."));
    }

    [Fact, Trait("Id", "BC-UT-124")]
    public void LoadedUser_HasNoPasswordField_ButShowsSignInStatus()
    {
        var page = RenderAs("admin");

        Search(page, "LEE");

        Value(page, "user-header-username").Should().Be("Lee");
        Value(page, "user-search-username").Should().Be("Lee");
        Selected(page, "user-header-role-id").Should().Be("2");
        page.FindAll("[data-testid=user-header-password]").Should().BeEmpty();
        Value(page, "user-header-failed-count").Should().Be("0");
        page.Find("[data-testid=user-header-failed-count]").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-125")]
    public void ThereIsNoDeleteButton()
    {
        var page = RenderAs("admin");
        Search(page, "lee");

        page.FindAll("[data-testid=user-toolbar-delete]").Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-128")]
    public void Unlock_IsEnabledOnlyForALockedUser_AndPostsTheAction()
    {
        _api.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(30);
        var page = RenderAs("admin");
        Search(page, "lee");

        page.Find("[data-testid=user-actions-unlock]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().Be("잠금을 해제했습니다."));
        _api.Actions.Should().Equal("/api/bc/users/5/unlock");
        page.Find("[data-testid=user-actions-unlock]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UT-129")]
    public void ResetPassword_WithDifferentPasswords_SaysSo_AndDoesNotPost()
    {
        var page = RenderAs("admin");
        Search(page, "lee");
        page.Find("[data-testid=user-actions-new-password]").Change("Reset-Pass-1");
        page.Find("[data-testid=user-actions-confirm-new-password]").Change("Reset-Pass-2");

        page.Find("[data-testid=user-actions-reset-password]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().Be("새 비밀번호와 확인 값이 같지 않습니다."));
        _api.Actions.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-129")]
    public void ResetPassword_AfterConfirming_PostsTheNewPassword()
    {
        var page = RenderAs("admin");
        Search(page, "lee");
        page.Find("[data-testid=user-actions-new-password]").Change("Reset-Pass-1");
        page.Find("[data-testid=user-actions-confirm-new-password]").Change("Reset-Pass-1");

        page.Find("[data-testid=user-actions-reset-password]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().StartWith("비밀번호를 초기화했습니다."));
        _confirm.Questions.Should().ContainSingle();
        _api.Actions.Should().Equal("/api/bc/users/5/reset-password");
        _api.ResetPasswords.Should().Equal("Reset-Pass-1");
        Value(page, "user-actions-new-password").Should().BeNullOrEmpty();
    }

    [Fact, Trait("Id", "BC-UT-129")]
    public void ResetPassword_BreakingAPasswordRule_ShowsTheAuthMessage()
    {
        var page = RenderAs("admin");
        Search(page, "lee");
        _api.ActionProblem = """{"status":400,"code":"AUTH-014"}""";
        page.Find("[data-testid=user-actions-new-password]").Change("lee");
        page.Find("[data-testid=user-actions-confirm-new-password]").Change("lee");

        page.Find("[data-testid=user-actions-reset-password]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().Be("비밀번호는 사용자 ID와 같을 수 없습니다."));
    }

    [Fact, Trait("Id", "BC-UT-130")]
    public void RevokeTokens_DeclinedDoesNothing_ConfirmedPosts()
    {
        var page = RenderAs("admin");
        Search(page, "lee");
        _confirm.Answer = false;

        page.Find("[data-testid=user-actions-revoke-tokens]").Click();
        _api.Actions.Should().BeEmpty();
        _confirm.Answer = true;
        page.Find("[data-testid=user-actions-revoke-tokens]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().Be("이 사용자의 자동 로그인을 모두 끊었습니다."));
        _api.Actions.Should().Equal("/api/bc/users/5/revoke-tokens");
    }

    [Fact, Trait("Id", "BC-UT-131")]
    public void MyOwnAccount_HasRoleAndInUseLocked()
    {
        var page = RenderAs("admin");

        Search(page, "kim");

        page.Find("[data-testid=user-header-role-id]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=user-header-valid-flag-n]").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=user-header-username]").HasAttribute("readonly").Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-UT-132")]
    public void UserWithoutFullAccess_CanOnlyRead()
    {
        var page = RenderAs("user");
        Search(page, "lee");

        page.FindAll("[role=toolbar] button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("user-toolbar-search", "user-toolbar-previous", "user-toolbar-next");
        page.FindAll("[data-testid=user-actions]").Should().BeEmpty();
        page.Find("[data-testid=user-header-username]").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-008-WT-02")]
    public void Lookup_SelectedUser_IsLoadedById()
    {
        var page = RenderAs("admin");

        page.Find("[data-testid=user-search-user-lookup]").Click();
        page.WaitForElement("[data-testid=user-user-lookup] tbody tr");
        page.Find("[data-testid=user-user-lookup] tbody tr button").Click();

        page.WaitForAssertion(() => Value(page, "user-header-userid").Should().Be("kim"));
        _api.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/bc/users/7");
    }

    [Fact, Trait("Id", "BC-008-WT-03")]
    public void Next_LoadsTheFollowingUser_AndPreviousAtTheFirstStays()
    {
        var page = RenderAs("admin");
        Search(page, "kim");

        page.Find("[data-testid=user-toolbar-next]").Click();
        page.WaitForAssertion(() => Value(page, "user-header-userid").Should().Be("lee"));
        page.Find("[data-testid=user-toolbar-previous]").Click();
        page.WaitForAssertion(() => Value(page, "user-header-userid").Should().Be("kim"));
        page.Find("[data-testid=user-toolbar-previous]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=user-message]").TextContent.Should().Be("처음 자료입니다."));
    }

    private IRenderedComponent<User> RenderAs(string role)
    {
        var store = Services.GetRequiredService<AccessTokenStore>();
        var sessionId = store.Add(new LoginResponse($"token-{role}", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", role, false, "ko"));
        _auth.SetAuthorized("kim");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(WebAuthClaims.SessionId, sessionId));
        var page = Render<User>();
        page.WaitForElement("[data-testid=user-header-userid]");
        page.WaitForAssertion(() => page.FindAll("[data-testid=user-header-role-id] option").Should().HaveCountGreaterThan(1));
        return page;
    }

    private static void Search(IRenderedComponent<User> page, string userId)
    {
        page.Find("[data-testid=user-search-userid]").Change(userId);
        page.Find("[data-testid=user-toolbar-search]").Click();
        page.WaitForAssertion(() => page.Find("[data-testid=user-header-userid]").HasAttribute("readonly").Should().BeTrue());
    }

    private static string? Value(IRenderedComponent<User> page, string testId) =>
        page.Find($"[data-testid={testId}]").GetAttribute("value");

    private static string? Selected(IRenderedComponent<User> page, string testId) =>
        page.Find($"[data-testid={testId}] option[selected]").GetAttribute("value");

    private static void Configure(HttpClient http) => http.BaseAddress = new Uri("http://api.test");

    /// <summary>User, role, code and localization API with two users: kim (7, admin, signed in) and lee (5, user).</summary>
    private sealed class FakeUserApi : HttpMessageHandler
    {
        private static readonly UserDto Kim = new(7, "kim", "Kim", 1, "admin", null, null, "ko", null, "Y", false, 0, null, null, null,
            DateTimeOffset.UnixEpoch);

        private UserDto _lee = new(5, "lee", "Lee", 2, "user", "lee@example.com", null, "ko", null, "Y", false, 0, null, null, null,
            DateTimeOffset.UnixEpoch);

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<CreateUserRequest> Created { get; } = [];

        public List<string> Actions { get; } = [];

        public List<string?> ResetPasswords { get; } = [];

        public (HttpStatusCode Status, string Body)? UpdateProblem { get; set; }

        public string? ActionProblem { get; set; }

        public DateTimeOffset? LockedUntil
        {
            set => _lee = _lee with { LockedUntil = value, FailedCount = value is null ? 0 : 5 };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/codes/", StringComparison.Ordinal))
            {
                return Json<IReadOnlyList<CodeItemDto>>(path.EndsWith("VALID_FLAG", StringComparison.Ordinal)
                    ? [new("Y", "사용"), new("N", "사용 안 함")]
                    : []);
            }
            if (path == "/" + LocalizationRoutes.Base)
            {
                return Json(new LocalizationDto("US", "USD", "en", "SALES_TAX", "MM/dd/yyyy", "DOCUMENT",
                    [new LanguageDto("ko", 1), new LanguageDto("en", 2)], [], [], []));
            }
            if (path == "/" + RoleRoutes.Base)
            {
                return Json<IReadOnlyList<RoleDto>>([new(1, "admin"), new(2, "user")]);
            }
            var users = new[] { Kim, _lee };
            if (request.Method == HttpMethod.Get && path == "/" + UserRoutes.Base)
            {
                return Json(new UserListDto(users, users.Length));
            }
            // User ID order: kim (7) is first, lee (5) is last
            if (request.Method == HttpMethod.Get && path is "/api/bc/users/7/previous" or "/api/bc/users/5/next")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Get && path == "/api/bc/users/7/next")
            {
                return Json(_lee);
            }
            if (request.Method == HttpMethod.Get && path == "/api/bc/users/5/previous")
            {
                return Json(Kim);
            }
            if (request.Method == HttpMethod.Get && path == "/api/bc/users/7")
            {
                return Json(Kim);
            }
            if (request.Method == HttpMethod.Get && path == "/api/bc/users/5")
            {
                return Json(_lee);
            }
            if (request.Method == HttpMethod.Post && path == "/" + UserRoutes.Base)
            {
                var create = (await request.Content!.ReadFromJsonAsync<CreateUserRequest>(cancellationToken))!;
                Created.Add(create);
                return Json(_lee with { Id = 9, UserId = create.UserId!.Trim().ToLowerInvariant(), UserName = create.UserName, MustChangePassword = true });
            }
            if (request.Method == HttpMethod.Post)
            {
                if (ActionProblem is not null)
                {
                    return Problem(HttpStatusCode.BadRequest, ActionProblem);
                }
                Actions.Add(path);
                if (path.EndsWith("/reset-password", StringComparison.Ordinal))
                {
                    ResetPasswords.Add((await request.Content!.ReadFromJsonAsync<ResetPasswordRequest>(cancellationToken))!.NewPassword);
                    _lee = _lee with { MustChangePassword = true };
                }
                if (path.EndsWith("/unlock", StringComparison.Ordinal))
                {
                    LockedUntil = null;
                }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Put)
            {
                if (UpdateProblem is { } problem)
                {
                    return Problem(problem.Status, problem.Body);
                }
                var update = (await request.Content!.ReadFromJsonAsync<UpdateUserRequest>(cancellationToken))!;
                _lee = _lee with { UserName = update.UserName };
                return Json(_lee);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        private static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, null, "application/problem+json") };
    }
}

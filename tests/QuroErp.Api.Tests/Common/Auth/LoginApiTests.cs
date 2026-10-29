using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Tests.Common.Auth;

/// <summary>Login API through the real host (BC-UC-001 test scenarios, login.md).</summary>
public class LoginApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Endpoint without [AllowAnonymous] or a policy of its own (development OpenAPI document)
    private const string ProtectedPath = "/openapi/v1.json";

    [Fact, Trait("Id", "BC-UC-001-T03")]
    public async Task Login_UnknownIdAndWrongPassword_Return401Auth002WithTheSameMessage()
    {
        var client = factory.CreateClient();

        using var unknown = await PostLoginAsync(client, new LoginRequest("nobody", ApiFactory.Password));
        using var wrong = await PostLoginAsync(client, new LoginRequest(ApiFactory.UserId, "wrong-password"));

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var unknownProblem = await unknown.ReadProblemAsync();
        var wrongProblem = await wrong.ReadProblemAsync();
        unknownProblem.GetProperty("code").GetString().Should().Be(AuthCodes.InvalidCredentials);
        unknownProblem.GetProperty("detail").GetString().Should().Be("아이디 또는 비밀번호가 올바르지 않습니다.")
            .And.Be(wrongProblem.GetProperty("detail").GetString());
    }

    [Theory, Trait("Id", "BC-UC-001-T01")]
    [InlineData("", "x", "사용자 ID을(를) 입력하세요.")]
    [InlineData("kim", "", "비밀번호을(를) 입력하세요.")]
    public async Task Login_EmptyField_Returns400Auth001NamingTheField(string loginId, string password, string detail)
    {
        using var response = await PostLoginAsync(factory.CreateClient(), new LoginRequest(loginId, password));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(AuthCodes.Required);
        problem.GetProperty("detail").GetString().Should().Be(detail);
    }

    [Fact, Trait("Id", "BC-UC-001-T05")]
    public async Task Login_FifthWrongPassword_Returns423Auth003WithLockedUntil_ThenCorrectPasswordStill423()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            using var failed = await PostLoginAsync(client, new LoginRequest(ApiFactory.LockUserId, "wrong-password"));
            failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var fifth = await PostLoginAsync(client, new LoginRequest(ApiFactory.LockUserId, "wrong-password"));
        using var correct = await PostLoginAsync(client, new LoginRequest(ApiFactory.LockUserId, ApiFactory.Password));

        fifth.StatusCode.Should().Be((HttpStatusCode)423);
        correct.StatusCode.Should().Be((HttpStatusCode)423);
        var problem = await correct.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(AuthCodes.Locked);
        problem.GetProperty("lockedUntil").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(60), TimeSpan.FromMinutes(1));
        problem.GetProperty("detail").GetString().Should().MatchRegex(@"\d{2}:\d{2} 이후");
        factory.LoginLogs.Should().Contain(l => l.LoginUserId == ApiFactory.LockUserId && l.EventType == LoginEvents.Locked);
    }

    [Fact, Trait("Id", "BC-UC-001-T12")]
    public async Task Login_MustChangePassword_OtherApisReturn403Auth005ButMeWorks()
    {
        var client = factory.CreateClient();
        var login = await LoginAsync(client, new LoginRequest(ApiFactory.NewUserId, ApiFactory.Password));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        using var other = await client.GetAsync(ProtectedPath);
        using var me = await client.GetAsync(AuthRoutes.Me);

        login.MustChangePassword.Should().BeTrue();
        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(AuthCodes.PasswordChangeRequired);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<MeResponse>())!.MustChangePassword.Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UC-001-T02")]
    public async Task Me_SignedIn_ReturnsUserMenusAndDefaultWarehouse()
    {
        var client = factory.CreateClient();
        var login = await LoginAsync(client, new LoginRequest(ApiFactory.UserId, ApiFactory.Password));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var me = await client.GetFromJsonAsync<MeResponse>(AuthRoutes.Me);

        me!.UserId.Should().Be(ApiFactory.UserId);
        me.Menus.Should().ContainSingle().Which.MenuCode.Should().Be("BC01");
        me.DefaultWarehouse!.WarehouseCode.Should().Be("W01");
    }

    [Fact, Trait("Id", "BC-UC-001-T19")]
    public async Task Me_WithoutToken_Returns401Auth006()
    {
        using var response = await factory.CreateClient().GetAsync(AuthRoutes.Me);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(AuthCodes.SessionExpired);
    }

    [Fact, Trait("Id", "BC-002-IT-01")]
    public async Task LoginOptions_Anonymous_ReturnsOnlyTheLoginScreenSettings()
    {
        factory.AuthSetting = new AuthSetting { AutoLoginEnabled = true, SessionIdleMinutes = 45, PasswordResetMode = "ADMIN_ONLY" };

        using var response = await factory.CreateClient().GetAsync(AuthRoutes.LoginOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<LoginOptionsResponse>()).Should().Be(new LoginOptionsResponse(true, "ADMIN_ONLY", 45));
        (await response.Content.ReadAsStringAsync()).Should().NotContain("maxFailedAttempts").And.NotContain("passwordMinLength");
    }

    [Fact, Trait("Id", "BC-UC-001-T15")]
    public async Task AutoLogin_RotatesTheToken_ReusingTheOldOneReturns401Auth007AndRevokesAll()
    {
        factory.AuthSetting = new AuthSetting { AutoLoginEnabled = true };
        var client = factory.CreateClient();
        var login = await LoginAsync(client, new LoginRequest(ApiFactory.UserId, ApiFactory.Password, AutoLogin: true));

        using var first = await client.PostAsJsonAsync(AuthRoutes.AutoLogin, new AutoLoginRequest(login.AutoLoginToken));
        var rotated = (await first.Content.ReadFromJsonAsync<LoginResponse>())!.AutoLoginToken;
        using var reuse = await client.PostAsJsonAsync(AuthRoutes.AutoLogin, new AutoLoginRequest(login.AutoLoginToken));
        using var afterTheft = await client.PostAsJsonAsync(AuthRoutes.AutoLogin, new AutoLoginRequest(rotated));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        rotated.Should().NotBe(login.AutoLoginToken);
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await reuse.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(AuthCodes.AutoLoginExpired);
        afterTheft.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-UC-001-T14")]
    public async Task Logout_RevokesThisDeviceTokenAndReturns204()
    {
        factory.AuthSetting = new AuthSetting { AutoLoginEnabled = true };
        var client = factory.CreateClient();
        var login = await LoginAsync(client, new LoginRequest(ApiFactory.UserId, ApiFactory.Password, AutoLogin: true));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        using var logout = await client.PostAsJsonAsync(AuthRoutes.Logout, new LogoutRequest(login.AutoLoginToken));
        using var autoLogin = await client.PostAsJsonAsync(AuthRoutes.AutoLogin, new AutoLoginRequest(login.AutoLoginToken));

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        autoLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.LoginLogs.Should().Contain(l => l.AppUserId == 7 && l.EventType == LoginEvents.Logout);
    }

    [Fact, Trait("Id", "BC-002-IT-02")]
    public async Task Logout_WithoutSession_RevokesTheTokenAloneAndAlwaysReturns204()
    {
        factory.AuthSetting = new AuthSetting { AutoLoginEnabled = true };
        var client = factory.CreateClient();
        var login = await LoginAsync(client, new LoginRequest(ApiFactory.UserId, ApiFactory.Password, AutoLogin: true));

        using var logout = await client.PostAsJsonAsync(AuthRoutes.Logout, new LogoutRequest(login.AutoLoginToken));
        using var unknown = await client.PostAsJsonAsync(AuthRoutes.Logout, new LogoutRequest("not-a-token"));
        using var autoLogin = await client.PostAsJsonAsync(AuthRoutes.AutoLogin, new AutoLoginRequest(login.AutoLoginToken));

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        unknown.StatusCode.Should().Be(HttpStatusCode.NoContent);
        autoLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-UC-001-T20")]
    public async Task AuthApis_EleventhRequestInAMinuteFromOneIp_Returns429Auth008()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Auth:RateLimit:PerMinute", "10"));
        var client = limited.CreateClient();
        for (var i = 0; i < 10; i++)
        {
            using var allowed = await PostLoginAsync(client, new LoginRequest("nobody", "x"));
            allowed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var eleventh = await client.GetAsync(AuthRoutes.Me);

        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await eleventh.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(AuthCodes.TooManyRequests);
    }

    [Theory, Trait("Id", "BC-UC-001-T21")]
    [InlineData("en", null, "Incorrect user ID or password.")]
    [InlineData(null, "en", "Incorrect user ID or password.")]
    [InlineData("en", "ko", "아이디 또는 비밀번호가 올바르지 않습니다.")]
    public async Task Login_Error_UsesTheRequestLanguage(string? acceptLanguage, string? bodyLanguage, string detail)
    {
        var client = factory.CreateClient();
        if (acceptLanguage is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(acceptLanguage));
        }

        using var response = await PostLoginAsync(client, new LoginRequest("nobody", "x", Language: bodyLanguage));

        var problem = await response.ReadProblemAsync();
        problem.GetProperty("title").GetString().Should().Be(detail);
        problem.GetProperty("detail").GetString().Should().Be(detail);
    }

    [Fact, Trait("Id", "BC-UC-001-T23")]
    public async Task Logs_NeverContainRawPasswordsOrTokens()
    {
        factory.AuthSetting = new AuthSetting { AutoLoginEnabled = true };
        var client = factory.CreateClient();
        const string wrongPassword = "Wrong-Secret-987";

        using var failed = await PostLoginAsync(client, new LoginRequest(ApiFactory.UserId, wrongPassword));
        var login = await LoginAsync(client, new LoginRequest(ApiFactory.UserId, ApiFactory.Password, AutoLogin: true));
        using var autoLogin = await client.PostAsJsonAsync(AuthRoutes.AutoLogin, new AutoLoginRequest(login.AutoLoginToken));

        factory.LogMessages.Should().NotBeEmpty();
        factory.LogMessages.Should().NotContain(m =>
            m.Contains(wrongPassword) || m.Contains(ApiFactory.Password) || m.Contains(login.AutoLoginToken!) || m.Contains(login.AccessToken));
        factory.LoginLogs.Should().NotContain(l => l.LoginUserId.Contains(ApiFactory.Password) || l.LoginUserId.Contains(wrongPassword));
    }

    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, LoginRequest request) =>
        client.PostAsJsonAsync(AuthRoutes.Login, request);

    private static async Task<LoginResponse> LoginAsync(HttpClient client, LoginRequest request)
    {
        using var response = await PostLoginAsync(client, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}

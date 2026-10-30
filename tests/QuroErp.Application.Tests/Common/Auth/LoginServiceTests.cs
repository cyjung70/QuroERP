using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Tests.Common.Auth;

/// <summary>LoginService on fakes (BC-UC-001 test scenarios, login.md). Time moves with FakeTimeProvider.</summary>
public class LoginServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private const string IpAddr = "10.0.0.5";
    private const string UserAgent = "Mozilla/5.0";

    private static readonly AuthClient Client = new(IpAddr, UserAgent);

    private readonly FakeUnitOfWorkFactory _unitOfWork = new();
    private readonly FakeAppUserRepository _users = new();
    private readonly FakeUserLoginLogRepository _logs = new();
    private readonly FakeAuthSettingRepository _settings = new();
    private readonly FakeAutoLoginTokenRepository _autoLoginTokens = new();
    private readonly FakePasswordHistoryRepository _history = new();
    private readonly FakePasswordHashService _passwords = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public LoginServiceTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _users.Users.Add(new AppUser
        {
            Id = 7,
            UserId = "kim",
            PasswordHash = "hash:secret",
            UserName = "Kim",
            RoleCode = "admin",
            ValidFlag = "Y",
        });
    }

    private AppUser Kim => _users.Users.Single(u => u.Id == 7);

    private LoginService Service =>
        new(_users, _logs, _settings, _autoLoginTokens, _history, _passwords, new FakeAccessTokenIssuer(), _unitOfWork, _clock);

    private Task<LoginResponse> LoginAsync(string loginId, string password, bool autoLogin = false, string? language = null) =>
        Service.LoginAsync(new LoginRequest(loginId, password, autoLogin, language), Client, CancellationToken.None);

    private static async Task<AuthException> FailAsync(Func<Task> act) =>
        (await act.Should().ThrowAsync<AuthException>()).Which;

    private async Task FailTimesAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await FailAsync(() => LoginAsync("kim", "wrong"));
        }
    }

    [Fact, Trait("Id", "BC-UC-001-T02")]
    public async Task LoginAsync_CorrectPassword_ReturnsTokenLogsSuccessAndResetsFailures()
    {
        await FailTimesAsync(2);

        var response = await LoginAsync("kim", "secret");

        response.Should().BeEquivalentTo(new LoginResponse("token-7", Now.AddHours(24), "kim", "Kim", "admin", false, null));
        Kim.FailedCount.Should().Be(0);
        Kim.LastLoginAt.Should().Be(Now);
        Kim.LastLoginIp.Should().Be(IpAddr);
        _logs.Logs.Last().Should().Be(
            new UserLoginLog(Now, 7, "kim", IpAddr, true, LoginEvents.LoginOk, null, UserAgent));
        _unitOfWork.Commits.Should().Be(3);
    }

    [Fact, Trait("Id", "BC-UC-001-T02")]
    public async Task LoginAsync_IdWithSpacesAndCapitals_IsTrimmedAndLowercasedAndLocksTheRow()
    {
        var response = await LoginAsync("  KIM ", "secret");

        response.UserId.Should().Be("kim");
        _users.Locked.Should().Equal("kim");
        _logs.Logs.Single().LoginUserId.Should().Be("  KIM ", "the log keeps the ID as typed");
    }

    [Fact, Trait("Id", "BC-UC-001-T02")]
    public async Task LoginAsync_SecondLogin_ReturnsThePreviousLoginTimeAndIp()
    {
        await LoginAsync("kim", "secret");
        _clock.Advance(TimeSpan.FromHours(1));

        var response = await LoginAsync("kim", "secret");

        response.LastLoginAt.Should().Be(Now);
        response.LastLoginIp.Should().Be(IpAddr);
    }

    [Theory, Trait("Id", "BC-UC-001-T01")]
    [InlineData("", "secret", AuthFields.LoginId)]
    [InlineData("  ", "secret", AuthFields.LoginId)]
    [InlineData("kim", "", AuthFields.Password)]
    public async Task LoginAsync_EmptyField_ThrowsAuth001WithoutLogging(string loginId, string password, string field)
    {
        var error = await FailAsync(() => LoginAsync(loginId, password));

        error.Code.Should().Be(AuthCodes.Required);
        error.Args.Should().Equal(field);
        _logs.Logs.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-001-T03")]
    public async Task LoginAsync_UnknownId_ThrowsAuth002LikeAWrongPasswordAndStillHashes()
    {
        var unknown = await FailAsync(() => LoginAsync("nobody", "secret"));
        var wrong = await FailAsync(() => LoginAsync("kim", "wrong"));

        unknown.Code.Should().Be(AuthCodes.InvalidCredentials);
        wrong.Code.Should().Be(unknown.Code);
        _passwords.HashCalls.Should().Be(1);
        _logs.Logs[0].Should().Be(
            new UserLoginLog(Now, null, "nobody", IpAddr, false, LoginEvents.LoginFail, AuthCodes.InvalidCredentials, UserAgent));
    }

    [Fact, Trait("Id", "BC-UC-001-T03")]
    public async Task LoginAsync_UnknownIdManyTimes_NeverLocks()
    {
        for (var i = 0; i < 7; i++)
        {
            (await FailAsync(() => LoginAsync("nobody", "secret"))).Code.Should().Be(AuthCodes.InvalidCredentials);
        }
    }

    [Fact, Trait("Id", "BC-UC-001-T04")]
    public async Task LoginAsync_WrongPasswordOnce_ThrowsAuth002AndCountsOne()
    {
        var error = await FailAsync(() => LoginAsync("kim", "wrong"));

        error.Code.Should().Be(AuthCodes.InvalidCredentials);
        error.RemainingAttempts.Should().BeNull("show_remaining_attempts is off");
        Kim.FailedCount.Should().Be(1);
        _users.Saved.Should().ContainSingle().Which.Should().Be((7L, 1, (DateTimeOffset?)null, (string?)null));
        _logs.Logs.Single().Should().Be(
            new UserLoginLog(Now, 7, "kim", IpAddr, false, LoginEvents.LoginFail, AuthCodes.InvalidCredentials, UserAgent));
        _unitOfWork.Commits.Should().Be(1, "the failure count is committed before the error");
    }

    [Fact, Trait("Id", "BC-UC-001-T04")]
    public async Task LoginAsync_ShowRemainingAttempts_ReturnsAttemptsLeft()
    {
        _settings.Setting = new AuthSetting { ShowRemainingAttempts = true };

        var error = await FailAsync(() => LoginAsync("kim", "wrong"));

        error.RemainingAttempts.Should().Be(4);
    }

    [Fact, Trait("Id", "BC-UC-001-T05")]
    public async Task LoginAsync_FifthFailure_LocksForSixtyMinutesFromTheLastFailure()
    {
        await FailTimesAsync(4);
        _clock.Advance(TimeSpan.FromMinutes(3));

        var error = await FailAsync(() => LoginAsync("kim", "wrong"));

        var lastFailure = Now.AddMinutes(3);
        error.Code.Should().Be(AuthCodes.Locked);
        error.LockedUntil.Should().Be(lastFailure.AddMinutes(60));
        Kim.LockedUntil.Should().Be(lastFailure.AddMinutes(60));
        _logs.Logs.Last().Should().Be(
            new UserLoginLog(lastFailure, 7, "kim", IpAddr, false, LoginEvents.Locked, AuthCodes.Locked, UserAgent));
    }

    [Fact, Trait("Id", "BC-UC-001-T05")]
    public async Task LoginAsync_SettingWithThreeAttempts_LocksOnTheThird()
    {
        _settings.Setting = new AuthSetting { MaxFailedAttempts = 3, LockoutMinutes = 10 };
        await FailTimesAsync(2);

        var error = await FailAsync(() => LoginAsync("kim", "wrong"));

        error.Code.Should().Be(AuthCodes.Locked);
        error.LockedUntil.Should().Be(Now.AddMinutes(10));
    }

    [Fact, Trait("Id", "BC-UC-001-T06")]
    public async Task LoginAsync_CorrectPasswordWhileLocked_ThrowsAuth003WithoutCountingOrExtending()
    {
        await FailTimesAsync(5);
        _clock.Advance(TimeSpan.FromMinutes(10));

        var right = await FailAsync(() => LoginAsync("kim", "secret"));
        var wrong = await FailAsync(() => LoginAsync("kim", "wrong"));

        right.Code.Should().Be(AuthCodes.Locked);
        wrong.Code.Should().Be(AuthCodes.Locked);
        wrong.LockedUntil.Should().Be(Now.AddMinutes(60));
        Kim.FailedCount.Should().Be(5);
        Kim.LockedUntil.Should().Be(Now.AddMinutes(60));
    }

    [Fact, Trait("Id", "BC-UC-001-T07")]
    public async Task LoginAsync_FiftyNineMinutesAfterLock_StillThrowsAuth003()
    {
        await FailTimesAsync(5);
        _clock.Advance(TimeSpan.FromMinutes(59));

        (await FailAsync(() => LoginAsync("kim", "secret"))).Code.Should().Be(AuthCodes.Locked);
    }

    [Fact, Trait("Id", "BC-UC-001-T08")]
    public async Task LoginAsync_SixtyMinutesAfterLock_ReleasesTheLockAndLogsIn()
    {
        await FailTimesAsync(5);
        _clock.Advance(TimeSpan.FromMinutes(60));

        var response = await LoginAsync("kim", "secret");

        response.UserId.Should().Be("kim");
        Kim.LockedUntil.Should().BeNull();
        Kim.FailedCount.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-001-T08")]
    public async Task LoginAsync_WrongPasswordAfterLockExpired_StartsCountingFromOne()
    {
        await FailTimesAsync(5);
        _clock.Advance(TimeSpan.FromMinutes(61));

        var error = await FailAsync(() => LoginAsync("kim", "wrong"));

        error.Code.Should().Be(AuthCodes.InvalidCredentials);
        Kim.FailedCount.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UC-001-T09")]
    public async Task LoginAsync_SuccessResetsTheCount_FourMoreFailuresDoNotLock()
    {
        await FailTimesAsync(3);
        await LoginAsync("kim", "secret");

        await FailTimesAsync(4);

        Kim.FailedCount.Should().Be(4);
        Kim.LockedUntil.Should().BeNull();
    }

    [Theory, Trait("Id", "BC-UC-001-T11")]
    [InlineData("N", null)]
    [InlineData("Y", "2026-10-24")]
    public async Task LoginAsync_InactiveUserWithCorrectPassword_ThrowsAuth004(string validFlag, string? userValidDt)
    {
        _users.Users.Add(new AppUser
        {
            Id = 8,
            UserId = "lee",
            PasswordHash = "hash:secret",
            ValidFlag = validFlag,
            UserValidDt = userValidDt is null ? null : DateOnly.Parse(userValidDt, System.Globalization.CultureInfo.InvariantCulture),
        });

        var error = await FailAsync(() => LoginAsync("lee", "secret"));

        error.Code.Should().Be(AuthCodes.Inactive);
        _logs.Logs.Single().ResultCode.Should().Be(AuthCodes.Inactive);
    }

    [Fact, Trait("Id", "BC-UC-001-T11")]
    public async Task LoginAsync_InactiveUserWithWrongPassword_ThrowsAuth002()
    {
        _users.Users.Add(new AppUser { Id = 8, UserId = "lee", PasswordHash = "hash:secret", ValidFlag = "N" });

        (await FailAsync(() => LoginAsync("lee", "wrong"))).Code.Should().Be(AuthCodes.InvalidCredentials);
    }

    [Fact, Trait("Id", "BC-UC-001-T12")]
    public async Task LoginAsync_MustChangePassword_ReturnsFlagAndMarksTheToken()
    {
        _users.Users.Add(new AppUser
        {
            Id = 9,
            UserId = "admin",
            PasswordHash = "hash:init",
            ValidFlag = "Y",
            MustChangePassword = true,
        });

        var response = await LoginAsync("admin", "init");

        response.MustChangePassword.Should().BeTrue();
        response.AccessToken.Should().Be("token-9-pwd");
    }

    [Fact, Trait("Id", "BC-UC-001-T12")]
    public async Task LoginAsync_PasswordOlderThanExpireDays_RequiresChange()
    {
        _settings.Setting = new AuthSetting { PasswordExpireDays = 90 };
        _users.Users.Add(new AppUser
        {
            Id = 9,
            UserId = "old",
            PasswordHash = "hash:secret",
            ValidFlag = "Y",
            PasswordChangedAt = Now.AddDays(-91),
        });

        (await LoginAsync("old", "secret")).MustChangePassword.Should().BeTrue();
    }

    [Fact, Trait("Id", "CORE-004-T09")]
    public async Task LoginAsync_LanguageChosenOnLoginScreen_IsReturnedAndSaved()
    {
        var response = await LoginAsync("kim", "secret", language: "en");

        response.Language.Should().Be("en");
        _users.Saved.Single().LangCode.Should().Be("en");
    }

    [Fact, Trait("Id", "CORE-004-T09")]
    public async Task LoginAsync_NoLanguageChosen_ReturnsTheUserLanguage()
    {
        _users.Users.Add(new AppUser { Id = 8, UserId = "lee", PasswordHash = "hash:secret", ValidFlag = "Y", LangCode = "en" });

        var response = await LoginAsync("lee", "secret", language: "fr");

        response.Language.Should().Be("en");
        _users.Saved.Single().LangCode.Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UC-001-T13")]
    public async Task LoginAsync_AutoLoginRequestedWhileSettingOff_IssuesNoToken()
    {
        var response = await LoginAsync("kim", "secret", autoLogin: true);

        response.AutoLoginToken.Should().BeNull();
        _autoLoginTokens.Tokens.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UC-001-T14")]
    public async Task LoginAsync_AutoLoginRequestedWhileSettingOn_StoresOnlyTheSha256Hash()
    {
        _settings.Setting = new AuthSetting { AutoLoginEnabled = true };

        var response = await LoginAsync("kim", "secret", autoLogin: true);

        response.AutoLoginToken.Should().HaveLength(64);
        response.AutoLoginExpiresAt.Should().Be(Now.AddDays(14));
        var stored = _autoLoginTokens.Tokens.Should().ContainSingle().Subject;
        stored.Key.Should().Be(AutoLoginToken.Hash(response.AutoLoginToken!)).And.NotBe(response.AutoLoginToken);
        stored.Value.ExpiresAt.Should().Be(Now.AddDays(14));
    }

    [Fact, Trait("Id", "BC-UC-001-T14")]
    public async Task AutoLoginAsync_ValidToken_RotatesTheTokenAndLogsAutoLogin()
    {
        var token = await SignInWithAutoLoginAsync();
        _clock.Advance(TimeSpan.FromDays(1));

        var response = await AutoLoginAsync(token);

        response.UserId.Should().Be("kim");
        response.AutoLoginToken.Should().NotBeNull().And.NotBe(token);
        response.AutoLoginExpiresAt.Should().Be(Now.AddDays(15));
        _autoLoginTokens.Tokens[AutoLoginToken.Hash(token)].RevokedAt.Should().Be(Now.AddDays(1));
        _autoLoginTokens.Tokens[AutoLoginToken.Hash(response.AutoLoginToken!)].RevokedAt.Should().BeNull();
        _logs.Logs.Last().Should().Be(
            new UserLoginLog(Now.AddDays(1), 7, "kim", IpAddr, true, LoginEvents.AutoLogin, null, UserAgent));
    }

    [Fact, Trait("Id", "BC-UC-001-T15")]
    public async Task AutoLoginAsync_RevokedTokenReused_ThrowsAuth007AndRevokesEveryTokenOfTheUser()
    {
        var token = await SignInWithAutoLoginAsync();
        var otherDevice = (await LoginAsync("kim", "secret", autoLogin: true)).AutoLoginToken!;
        var rotated = (await AutoLoginAsync(token)).AutoLoginToken!;

        var error = await FailAsync(() => AutoLoginAsync(token));

        error.Code.Should().Be(AuthCodes.AutoLoginExpired);
        _autoLoginTokens.Tokens.Values.Should().OnlyContain(t => t.RevokedAt != null);
        (await FailAsync(() => AutoLoginAsync(rotated))).Code.Should().Be(AuthCodes.AutoLoginExpired);
        (await FailAsync(() => AutoLoginAsync(otherDevice))).Code.Should().Be(AuthCodes.AutoLoginExpired);
        _logs.Logs.Last().ResultCode.Should().Be(AuthCodes.AutoLoginExpired);
    }

    [Fact, Trait("Id", "BC-UC-001-T16")]
    public async Task AutoLoginAsync_TokenIssuedBeforeThePasswordChange_ThrowsAuth007()
    {
        _settings.Setting = new AuthSetting { AutoLoginEnabled = true };
        _users.Users.Add(new AppUser
        {
            Id = 8,
            UserId = "lee",
            PasswordHash = "hash:secret",
            ValidFlag = "Y",
            PasswordChangedAt = Now.AddMinutes(1),
        });
        var token = AutoLoginToken.Create();
        await _autoLoginTokens.InsertAsync(8, AutoLoginToken.Hash(token), Now.AddDays(14), null, Now, null!, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));

        (await FailAsync(() => AutoLoginAsync(token))).Code.Should().Be(AuthCodes.AutoLoginExpired);
        _autoLoginTokens.Tokens.Values.Single().RevokedAt.Should().NotBeNull();
    }

    [Fact, Trait("Id", "BC-UC-001-T17")]
    public async Task AutoLoginAsync_AccountLockedMeanwhile_ThrowsAuth007AndRevokesTheToken()
    {
        var token = await SignInWithAutoLoginAsync();
        await FailTimesAsync(5);

        (await FailAsync(() => AutoLoginAsync(token))).Code.Should().Be(AuthCodes.AutoLoginExpired);
        _autoLoginTokens.Tokens[AutoLoginToken.Hash(token)].RevokedAt.Should().NotBeNull();
    }

    [Fact, Trait("Id", "BC-UC-001-T14")]
    public async Task AutoLoginAsync_ExpiredToken_ThrowsAuth007()
    {
        var token = await SignInWithAutoLoginAsync();
        _clock.Advance(TimeSpan.FromDays(14));

        (await FailAsync(() => AutoLoginAsync(token))).Code.Should().Be(AuthCodes.AutoLoginExpired);
    }

    [Fact, Trait("Id", "BC-UC-001-T13")]
    public async Task AutoLoginAsync_SettingTurnedOff_ThrowsAuth007()
    {
        var token = await SignInWithAutoLoginAsync();
        _settings.Setting = new AuthSetting { AutoLoginEnabled = false };

        (await FailAsync(() => AutoLoginAsync(token))).Code.Should().Be(AuthCodes.AutoLoginExpired);
    }

    [Fact, Trait("Id", "BC-UC-001-T15")]
    public async Task AutoLoginAsync_UnknownToken_ThrowsAuth007()
    {
        _settings.Setting = new AuthSetting { AutoLoginEnabled = true };

        (await FailAsync(() => AutoLoginAsync("not-a-token"))).Code.Should().Be(AuthCodes.AutoLoginExpired);
    }

    [Fact, Trait("Id", "BC-UC-001-T14")]
    public async Task LogoutAsync_RevokesThisDeviceTokenOnlyAndLogsLogout()
    {
        var token = await SignInWithAutoLoginAsync();
        var otherDevice = (await LoginAsync("kim", "secret", autoLogin: true)).AutoLoginToken!;

        await Service.LogoutAsync(7, new LogoutRequest(token), Client, CancellationToken.None);

        _autoLoginTokens.Tokens[AutoLoginToken.Hash(token)].RevokedAt.Should().Be(Now);
        _autoLoginTokens.Tokens[AutoLoginToken.Hash(otherDevice)].RevokedAt.Should().BeNull();
        _logs.Logs.Last().Should().Be(new UserLoginLog(Now, 7, "kim", IpAddr, true, LoginEvents.Logout, null, UserAgent));
    }

    [Fact, Trait("Id", "BC-002-UT-02")]
    public async Task LogoutAsync_NoSession_RevokesOnlyTheGivenTokenAndLogsItsUser()
    {
        var token = await SignInWithAutoLoginAsync();
        var otherDevice = (await LoginAsync("kim", "secret", autoLogin: true)).AutoLoginToken!;

        await Service.LogoutAsync(null, new LogoutRequest(token), Client, CancellationToken.None);

        _autoLoginTokens.Tokens[AutoLoginToken.Hash(token)].RevokedAt.Should().Be(Now);
        _autoLoginTokens.Tokens[AutoLoginToken.Hash(otherDevice)].RevokedAt.Should().BeNull();
        _logs.Logs.Last().Should().Be(new UserLoginLog(Now, 7, "kim", IpAddr, true, LoginEvents.Logout, null, UserAgent));
    }

    [Theory, Trait("Id", "BC-002-UT-03")]
    [InlineData(null)]
    [InlineData("not-a-token")]
    [InlineData("revoked")]
    public async Task LogoutAsync_NoSession_NoLiveToken_DoesNothing(string? kind)
    {
        var token = await SignInWithAutoLoginAsync();
        var otherDevice = (await LoginAsync("kim", "secret", autoLogin: true)).AutoLoginToken!;
        if (kind == "revoked")
        {
            await Service.LogoutAsync(7, new LogoutRequest(token), Client, CancellationToken.None);
        }
        var logs = _logs.Logs.Count;

        await Service.LogoutAsync(null, new LogoutRequest(kind == "revoked" ? token : kind), Client, CancellationToken.None);

        _logs.Logs.Should().HaveCount(logs);
        // A revoked token at logout is not a theft signal: the other device keeps its token
        _autoLoginTokens.Tokens[AutoLoginToken.Hash(otherDevice)].RevokedAt.Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UC-001-T02")]
    public async Task MeAsync_ReturnsUserMenusAndDefaultWarehouse()
    {
        _users.Menus.Add(new AuthMenu("BC01", "Company", "bc-company", "BC", null, "Y"));
        _users.DefaultWarehouse = new AuthWarehouse(1, "W01", "Main");

        var me = await Service.MeAsync(7, CancellationToken.None);

        me.Should().BeEquivalentTo(new
        {
            UserId = "kim",
            RoleCode = "admin",
            MustChangePassword = false,
            DefaultWarehouse = new AuthWarehouse(1, "W01", "Main"),
            Menus = new[] { new AuthMenu("BC01", "Company", "bc-company", "BC", null, "Y") },
        });
    }

    [Fact, Trait("Id", "BC-UC-001-T19")]
    public async Task MeAsync_UserNoLongerActive_ThrowsAuth006()
    {
        _users.Users.Add(new AppUser { Id = 8, UserId = "lee", ValidFlag = "N" });

        (await FailAsync(() => Service.MeAsync(8, CancellationToken.None))).Code.Should().Be(AuthCodes.SessionExpired);
    }

    [Fact, Trait("Id", "CORE-003-T16")]
    public async Task RefreshAsync_ActiveUser_ReturnsNewToken()
    {
        var response = await Service.RefreshAsync(7, 0, CancellationToken.None);

        response!.AccessToken.Should().Be("token-7");
        _logs.Logs.Should().BeEmpty();
    }

    [Fact, Trait("Id", "CORE-003-T17")]
    public async Task RefreshAsync_UserNoLongerValid_ReturnsNull()
    {
        _users.Users.Add(new AppUser { Id = 10, UserId = "park", ValidFlag = "Y", UserValidDt = new DateOnly(2026, 10, 24) });

        (await Service.RefreshAsync(10, 0, CancellationToken.None)).Should().BeNull();
    }

    [Fact, Trait("Id", "BC-UC-001-T06")]
    public async Task RefreshAsync_LockedUser_ReturnsNull()
    {
        await FailTimesAsync(5);

        (await Service.RefreshAsync(7, 0, CancellationToken.None)).Should().BeNull();
    }

    private async Task<string> SignInWithAutoLoginAsync()
    {
        _settings.Setting = new AuthSetting { AutoLoginEnabled = true };
        return (await LoginAsync("kim", "secret", autoLogin: true)).AutoLoginToken!;
    }

    private Task<LoginResponse> AutoLoginAsync(string token) =>
        Service.AutoLoginAsync(new AutoLoginRequest(token), Client, CancellationToken.None);
}

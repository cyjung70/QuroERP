using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Tests.Common.Auth;

/// <summary>LoginService.ChangePasswordAsync and the token version on refresh (BC-UC-002, BC-BR-051~059, BC-BR-130).</summary>
public class ChangePasswordTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private const string Current = "Current-Pass-1";

    private readonly FakeUnitOfWorkFactory _unitOfWork = new();
    private readonly FakeAppUserRepository _users = new();
    private readonly FakeAuthSettingRepository _settings = new();
    private readonly FakeAutoLoginTokenRepository _autoLoginTokens = new();
    private readonly FakePasswordHistoryRepository _history = new();
    private readonly FakePasswordHashService _passwords = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public ChangePasswordTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _users.Users.Add(new AppUser
        {
            Id = 7,
            UserId = "kim",
            PasswordHash = "hash:" + Current,
            UserName = "Kim",
            RoleCode = "admin",
            ValidFlag = "Y",
            MustChangePassword = true,
            PasswordValidDt = new DateOnly(2026, 1, 1),
        });
    }

    private AppUser Kim => _users.Users.Single(u => u.Id == 7);

    private LoginService Service =>
        new(_users, new FakeUserLoginLogRepository(), _settings, _autoLoginTokens, _history, _passwords,
            new FakeAccessTokenIssuer(), _unitOfWork, _clock);

    private Task<LoginResponse> ChangeAsync(string? current, string? next) =>
        Service.ChangePasswordAsync(7, new ChangePasswordRequest(current, next), CancellationToken.None);

    private async Task<AuthException> FailAsync(string? current, string? next) =>
        (await FluentActions.Awaiting(() => ChangeAsync(current, next)).Should().ThrowAsync<AuthException>()).Which;

    [Fact, Trait("Id", "BC-UC-002-T01")]
    public async Task Change_Valid_SavesHashClearsForcedChangeAndReturnsNewToken()
    {
        var response = await ChangeAsync(Current, "Spring-2026");

        response.AccessToken.Should().Be("token-7-v1", "the new token carries the new version and no forced change");
        response.MustChangePassword.Should().BeFalse();
        response.AutoLoginToken.Should().BeNull();
        Kim.PasswordHash.Should().Be("hash:Spring-2026");
        Kim.PasswordChangedAt.Should().Be(Now);
        Kim.PasswordValidDt.Should().BeNull("a legacy password valid date would force the change again (BC-BR-127)");
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-058")]
    public async Task Change_AddsOldHashToHistory_BumpsVersion_AndRevokesEveryAutoLoginToken()
    {
        await _autoLoginTokens.InsertAsync(7, "a", Now.AddDays(14), null, Now, null!, CancellationToken.None);
        await _autoLoginTokens.InsertAsync(7, "b", Now.AddDays(14), null, Now, null!, CancellationToken.None);

        await ChangeAsync(Current, "Spring-2026");

        _history.Rows.Should().Equal((7L, "hash:" + Current, "kim"));
        Kim.TokenVersion.Should().Be(1);
        _autoLoginTokens.Tokens.Values.Should().OnlyContain(t => t.RevokedAt == Now);
    }

    [Theory, Trait("Id", "BC-UC-002-T02")]
    [InlineData(null, "Spring-2026", AuthFields.CurrentPassword)]
    [InlineData("", "Spring-2026", AuthFields.CurrentPassword)]
    [InlineData(Current, null, AuthFields.NewPassword)]
    public async Task Change_MissingInput_IsAuth001WithField(string? current, string? next, string field)
    {
        var failure = await FailAsync(current, next);

        failure.Code.Should().Be(AuthCodes.Required);
        failure.Args.Should().Equal(field);
        _unitOfWork.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-052")]
    public async Task Change_WrongCurrentPassword_IsAuth015_EvenForAForcedChange()
    {
        (await FailAsync("wrong", "Spring-2026")).Code.Should().Be(AuthCodes.CurrentPasswordIncorrect);

        Kim.PasswordHash.Should().Be("hash:" + Current);
        _history.Rows.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-059")]
    public async Task Change_WrongCurrentAndWeakNew_ReportsAuth015First()
    {
        (await FailAsync("wrong", "x")).Code.Should().Be(AuthCodes.CurrentPasswordIncorrect);
    }

    [Theory, Trait("Id", "BC-UT-053")]
    [InlineData("Ab1!", AuthCodes.PasswordLength, 8)]
    [InlineData("abcdefgh1", AuthCodes.PasswordCategories, 3)]
    public async Task Change_PolicyViolation_IsAuthCodeWithSettingValue(string next, string code, int arg)
    {
        var failure = await FailAsync(Current, next);

        failure.Code.Should().Be(code);
        failure.Args.Should().Equal(arg);
    }

    [Fact, Trait("Id", "BC-UT-053")]
    public async Task Change_UsesThePolicyValuesOfTheSetting()
    {
        _settings.Setting = new AuthSetting { PasswordMinLength = 12 };

        (await FailAsync(Current, "Spring-2026")).Args.Should().Equal(12);
    }

    [Fact, Trait("Id", "BC-UT-055")]
    public async Task Change_CurrentOrTwoPrevious_IsAuth013_ButTheThirdPreviousIsAllowed()
    {
        _history.Rows.Add((7, "hash:Third-Ago-1", "kim"));
        _history.Rows.Add((7, "hash:Second-Ago-1", "kim"));
        _history.Rows.Add((7, "hash:Last-Pass-1", "kim"));

        (await FailAsync(Current, Current)).Code.Should().Be(AuthCodes.PasswordReused);
        (await FailAsync(Current, "Last-Pass-1")).Code.Should().Be(AuthCodes.PasswordReused);
        (await FailAsync(Current, "Second-Ago-1")).Code.Should().Be(AuthCodes.PasswordReused);
        (await ChangeAsync(Current, "Third-Ago-1")).MustChangePassword.Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-UT-056")]
    public async Task Change_SameAsUserId_IsAuth014()
    {
        _users.Users[0] = new AppUser { Id = 7, UserId = "kim.user1", PasswordHash = "hash:" + Current, ValidFlag = "Y" };

        (await FailAsync(Current, "KIM.USER1")).Code.Should().Be(AuthCodes.PasswordSameAsUserId);
    }

    [Fact, Trait("Id", "BC-UT-051")]
    public async Task Change_UserNoLongerActive_IsAuth006()
    {
        _users.Users[0] = new AppUser { Id = 7, UserId = "kim", PasswordHash = "hash:" + Current, ValidFlag = "N" };

        (await FailAsync(Current, "Spring-2026")).Code.Should().Be(AuthCodes.SessionExpired);
    }

    [Fact, Trait("Id", "BC-UT-130")]
    public async Task Refresh_AfterChange_RejectsTheOldVersionAndAcceptsTheNewOne()
    {
        await ChangeAsync(Current, "Spring-2026");

        (await Service.RefreshAsync(7, 0, CancellationToken.None)).Should().BeNull();
        (await Service.RefreshAsync(7, 1, CancellationToken.None))!.AccessToken.Should().Be("token-7-v1");
    }
}

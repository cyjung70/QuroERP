using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Tests.Common.Auth;

public class LoginServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private const string IpAddr = "10.0.0.5";

    private readonly FakeUnitOfWorkFactory _unitOfWork = new();
    private readonly FakeAppUserRepository _users = new();
    private readonly FakeUserLoginLogRepository _logs = new();
    private readonly FakePasswordHashService _passwords = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public LoginServiceTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _users.Users.Add(new AppUser
        {
            Id = 7, UserId = "kim", PasswordHash = "hash:secret", UserName = "Kim", RoleCode = "admin", ValidFlag = "Y",
        });
    }

    private LoginService Service => new(_users, _logs, _passwords, new FakeAccessTokenIssuer(), _unitOfWork, _clock);

    [Fact, Trait("Id", "CORE-003-T11")]
    public async Task LoginAsync_CorrectPassword_ReturnsTokenAndLogsSuccess()
    {
        var response = await Service.LoginAsync(new LoginRequest("kim", "secret"), IpAddr, CancellationToken.None);

        response.Should().BeEquivalentTo(new LoginResponse("token-7", Now.AddHours(24), "kim", "Kim", "admin", false));
        _logs.Logs.Should().ContainSingle().Which.Should().Be(new UserLoginLog(Now, 7, "kim", IpAddr, Success: true));
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "CORE-003-T12")]
    public async Task LoginAsync_WrongPassword_ReturnsNullAndLogsFailure()
    {
        var response = await Service.LoginAsync(new LoginRequest("kim", "wrong"), IpAddr, CancellationToken.None);

        response.Should().BeNull();
        _logs.Logs.Should().ContainSingle().Which.Should().Be(new UserLoginLog(Now, 7, "kim", IpAddr, Success: false));
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "CORE-003-T13")]
    public async Task LoginAsync_UnknownUserId_LogsFailureWithoutUserAndStillHashes()
    {
        var response = await Service.LoginAsync(new LoginRequest("nobody", "secret"), IpAddr, CancellationToken.None);

        response.Should().BeNull();
        _logs.Logs.Should().ContainSingle().Which.Should().Be(new UserLoginLog(Now, null, "nobody", IpAddr, Success: false));
        _passwords.HashCalls.Should().Be(1);
    }

    [Fact, Trait("Id", "CORE-003-T14")]
    public async Task LoginAsync_UnusedUser_ReturnsNullAndLogsFailure()
    {
        _users.Users.Add(new AppUser { Id = 8, UserId = "lee", PasswordHash = "hash:secret", ValidFlag = "N" });

        var response = await Service.LoginAsync(new LoginRequest("lee", "secret"), IpAddr, CancellationToken.None);

        response.Should().BeNull();
        _logs.Logs.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-003-T15")]
    public async Task LoginAsync_MustChangePassword_ReturnsFlag()
    {
        _users.Users.Add(new AppUser
        {
            Id = 9, UserId = "admin", PasswordHash = "hash:init", ValidFlag = "Y", MustChangePassword = true,
        });

        var response = await Service.LoginAsync(new LoginRequest("admin", "init"), IpAddr, CancellationToken.None);

        response!.MustChangePassword.Should().BeTrue();
    }

    [Fact, Trait("Id", "CORE-003-T16")]
    public async Task RefreshAsync_ActiveUser_ReturnsNewToken()
    {
        var response = await Service.RefreshAsync(7, CancellationToken.None);

        response!.AccessToken.Should().Be("token-7");
        _logs.Logs.Should().BeEmpty();
    }

    [Fact, Trait("Id", "CORE-003-T17")]
    public async Task RefreshAsync_UserNoLongerValid_ReturnsNull()
    {
        _users.Users.Add(new AppUser { Id = 10, UserId = "park", ValidFlag = "Y", UserValidDt = new DateOnly(2026, 10, 24) });

        (await Service.RefreshAsync(10, CancellationToken.None)).Should().BeNull();
    }
}

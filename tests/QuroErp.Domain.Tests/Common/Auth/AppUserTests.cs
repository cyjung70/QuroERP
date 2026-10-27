using FluentAssertions;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Domain.Tests.Common.Auth;

public class AppUserTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact, Trait("Id", "CORE-003-T01")]
    public void CanLogin_ValidFlagY_ReturnsTrue()
    {
        new AppUser { ValidFlag = "Y" }.CanLogin(Today).Should().BeTrue();
    }

    [Theory, Trait("Id", "CORE-003-T02")]
    [InlineData("N")]
    [InlineData("S")]
    [InlineData(null)]
    public void CanLogin_ValidFlagNotY_ReturnsFalse(string? validFlag)
    {
        new AppUser { ValidFlag = validFlag }.CanLogin(Today).Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-003-T03")]
    public void CanLogin_UserValidDtPassed_ReturnsFalse()
    {
        var user = new AppUser { ValidFlag = "Y", UserValidDt = Today.AddDays(-1) };

        user.CanLogin(Today).Should().BeFalse();
    }

    [Fact, Trait("Id", "CORE-003-T04")]
    public void CanLogin_UserValidDtToday_ReturnsTrue()
    {
        var user = new AppUser { ValidFlag = "Y", UserValidDt = Today };

        user.CanLogin(Today).Should().BeTrue();
    }

    [Fact, Trait("Id", "CORE-003-T05")]
    public void RequiresPasswordChange_MustChangePassword_ReturnsTrue()
    {
        new AppUser { MustChangePassword = true }.RequiresPasswordChange(Today).Should().BeTrue();
    }

    [Fact, Trait("Id", "CORE-003-T06")]
    public void RequiresPasswordChange_PasswordValidDtPassed_ReturnsTrue()
    {
        new AppUser { PasswordValidDt = Today.AddDays(-1) }.RequiresPasswordChange(Today).Should().BeTrue();
    }

    [Fact, Trait("Id", "CORE-003-T07")]
    public void RequiresPasswordChange_NoFlagAndValidPassword_ReturnsFalse()
    {
        new AppUser { PasswordValidDt = Today }.RequiresPasswordChange(Today).Should().BeFalse();
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly AuthSetting Setting = new();

    [Theory, Trait("Id", "BC-UC-001-T02")]
    [InlineData("kim", "kim")]
    [InlineData("  Kim ", "kim")]
    [InlineData("ADMIN", "admin")]
    public void NormalizeUserId_TrimsAndLowercases(string typed, string expected)
    {
        AppUser.NormalizeUserId(typed).Should().Be(expected);
    }

    [Fact, Trait("Id", "BC-UC-001-T05")]
    public void RecordFailure_FifthFailure_LocksUntilNowPlusLockout()
    {
        var user = new AppUser();

        var locked = Enumerable.Range(0, 5).Select(_ => user.RecordFailure(Now, Setting)).ToList();

        locked.Should().Equal(false, false, false, false, true);
        user.FailedCount.Should().Be(5);
        user.LockedUntil.Should().Be(Now.AddMinutes(60));
        user.IsLocked(Now.AddMinutes(59)).Should().BeTrue();
        user.IsLocked(Now.AddMinutes(60)).Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-UC-001-T08")]
    public void ReleaseExpiredLock_AfterTheLock_ClearsLockAndCount()
    {
        var user = new AppUser();
        for (var i = 0; i < 5; i++)
        {
            user.RecordFailure(Now, Setting);
        }

        user.ReleaseExpiredLock(Now.AddMinutes(59));
        user.LockedUntil.Should().NotBeNull();

        user.ReleaseExpiredLock(Now.AddMinutes(60));
        user.LockedUntil.Should().BeNull();
        user.FailedCount.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-001-T09")]
    public void RecordSuccess_ResetsFailuresAndStoresTheLogin()
    {
        var user = new AppUser();
        user.RecordFailure(Now, Setting);

        user.RecordSuccess(Now, "10.0.0.1");

        user.FailedCount.Should().Be(0);
        user.LastLoginAt.Should().Be(Now);
        user.LastLoginIp.Should().Be("10.0.0.1");
        user.RemainingAttempts(Setting).Should().Be(5);
    }

    [Theory, Trait("Id", "BC-UC-001-T12")]
    [InlineData(0, -400, false)]
    [InlineData(90, -89, false)]
    [InlineData(90, -91, true)]
    public void RequiresPasswordChange_ExpireDays_ForcesChangeForOldPasswords(int expireDays, int changedDaysAgo, bool expected)
    {
        var user = new AppUser { PasswordChangedAt = Now.AddDays(changedDaysAgo) };

        user.RequiresPasswordChange(Now, Today, new AuthSetting { PasswordExpireDays = expireDays }).Should().Be(expected);
    }
}

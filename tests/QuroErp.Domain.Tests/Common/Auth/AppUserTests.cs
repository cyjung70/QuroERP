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
}

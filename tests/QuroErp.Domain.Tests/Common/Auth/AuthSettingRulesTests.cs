using FluentAssertions;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Domain.Tests.Common.Auth;

/// <summary>Security setting rules (BC-BR-309, BC-BR-310).</summary>
public class AuthSettingRulesTests
{
    [Theory, Trait("Id", "BC-UT-309")]
    [InlineData("MaxFailedAttempts", 2)]
    [InlineData("MaxFailedAttempts", 11)]
    [InlineData("LockoutMinutes", 0)]
    [InlineData("LockoutMinutes", 1441)]
    [InlineData("AutoLoginValidDays", 91)]
    [InlineData("SessionIdleMinutes", 4)]
    [InlineData("SessionIdleMinutes", 481)]
    [InlineData("PasswordMinLength", 7)]
    [InlineData("PasswordMinLength", 129)]
    [InlineData("PasswordRequiredCategories", 5)]
    [InlineData("PasswordHistoryCount", 25)]
    [InlineData("PasswordExpireDays", -1)]
    [InlineData("PasswordExpireDays", 3651)]
    public void OutOfRange_IsBcE309WithTheField(string field, int value)
    {
        var act = () => AuthSettingRules.Normalize(With(field, value));

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(AuthSettingRules.InvalidValueCode);
        error.Args.Should().Equal(field);
    }

    [Fact, Trait("Id", "BC-UT-309")]
    public void Limits_AreAllowed_AndTheResetModeIsUpperCase()
    {
        var setting = AuthSettingRules.Normalize(new AuthSetting
        {
            MaxFailedAttempts = 10,
            LockoutMinutes = 1440,
            AutoLoginValidDays = 90,
            SessionIdleMinutes = 5,
            PasswordMinLength = 128,
            PasswordRequiredCategories = 1,
            PasswordHistoryCount = 0,
            PasswordExpireDays = 3650,
            PasswordResetMode = " email ",
        });

        setting.PasswordResetMode.Should().Be("EMAIL");
        setting.PasswordMinLength.Should().Be(128);
    }

    [Fact, Trait("Id", "BC-UT-309")]
    public void UnknownResetMode_IsBcE309()
    {
        var act = () => AuthSettingRules.Normalize(new AuthSetting { PasswordResetMode = "SMS" });

        act.Should().Throw<DomainException>().Which.Args.Should().Equal("PasswordResetMode");
    }

    [Theory, Trait("Id", "BC-UT-310")]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void OnlyTurningAutoLoginOff_RevokesTokens(bool before, bool after, bool expected) =>
        AuthSettingRules.RevokesAutoLoginTokens(new AuthSetting { AutoLoginEnabled = before }, new AuthSetting { AutoLoginEnabled = after })
            .Should().Be(expected);

    private static AuthSetting With(string field, int value) => field switch
    {
        "MaxFailedAttempts" => new AuthSetting { MaxFailedAttempts = value },
        "LockoutMinutes" => new AuthSetting { LockoutMinutes = value },
        "AutoLoginValidDays" => new AuthSetting { AutoLoginValidDays = value },
        "SessionIdleMinutes" => new AuthSetting { SessionIdleMinutes = value },
        "PasswordMinLength" => new AuthSetting { PasswordMinLength = value },
        "PasswordRequiredCategories" => new AuthSetting { PasswordRequiredCategories = value },
        "PasswordHistoryCount" => new AuthSetting { PasswordHistoryCount = value },
        _ => new AuthSetting { PasswordExpireDays = value },
    };
}

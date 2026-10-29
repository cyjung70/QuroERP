namespace QuroErp.Domain.Common.Auth;

/// <summary>Security setting rules BC-BR-309 and BC-BR-310 (login.md "설정").</summary>
public static class AuthSettingRules
{
    /// <summary>A value is out of range. Arg: property name.</summary>
    public const string InvalidValueCode = "BC-E-309";

    public const string AdminOnly = "ADMIN_ONLY";
    public const string Email = "EMAIL";

    private static readonly (string Field, Func<AuthSetting, int> Value, int Min, int Max)[] Ranges =
    [
        (nameof(AuthSetting.MaxFailedAttempts), s => s.MaxFailedAttempts, 3, 10),
        (nameof(AuthSetting.LockoutMinutes), s => s.LockoutMinutes, 1, 1440),
        (nameof(AuthSetting.AutoLoginValidDays), s => s.AutoLoginValidDays, 1, 90),
        (nameof(AuthSetting.SessionIdleMinutes), s => s.SessionIdleMinutes, 5, 480),
        (nameof(AuthSetting.PasswordMinLength), s => s.PasswordMinLength, 8, 128),
        (nameof(AuthSetting.PasswordRequiredCategories), s => s.PasswordRequiredCategories, 1, 4),
        (nameof(AuthSetting.PasswordHistoryCount), s => s.PasswordHistoryCount, 0, 24),
        (nameof(AuthSetting.PasswordExpireDays), s => s.PasswordExpireDays, 0, 3650),
    ];

    /// <summary>BC-BR-309. The reset mode is upper-cased.</summary>
    /// <exception cref="DomainException">BC-E-309 with the property name.</exception>
    public static AuthSetting Normalize(AuthSetting setting)
    {
        foreach (var (field, value, min, max) in Ranges)
        {
            if (value(setting) < min || value(setting) > max)
            {
                throw new DomainException(InvalidValueCode, field);
            }
        }
        var resetMode = setting.PasswordResetMode?.Trim().ToUpperInvariant();
        if (resetMode is not (AdminOnly or Email))
        {
            throw new DomainException(InvalidValueCode, nameof(AuthSetting.PasswordResetMode));
        }
        return new AuthSetting
        {
            MaxFailedAttempts = setting.MaxFailedAttempts,
            LockoutMinutes = setting.LockoutMinutes,
            ShowRemainingAttempts = setting.ShowRemainingAttempts,
            AutoLoginEnabled = setting.AutoLoginEnabled,
            AutoLoginValidDays = setting.AutoLoginValidDays,
            SessionIdleMinutes = setting.SessionIdleMinutes,
            AllowConcurrentLogin = setting.AllowConcurrentLogin,
            PasswordMinLength = setting.PasswordMinLength,
            PasswordRequiredCategories = setting.PasswordRequiredCategories,
            PasswordExpireDays = setting.PasswordExpireDays,
            PasswordHistoryCount = setting.PasswordHistoryCount,
            PasswordResetMode = resetMode,
        };
    }

    /// <summary>BC-BR-310: turning auto login off revokes every user's auto login token in the same transaction.</summary>
    public static bool RevokesAutoLoginTokens(AuthSetting current, AuthSetting next) => current.AutoLoginEnabled && !next.AutoLoginEnabled;
}

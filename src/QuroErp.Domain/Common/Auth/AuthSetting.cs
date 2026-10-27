namespace QuroErp.Domain.Common.Auth;

/// <summary>Authentication setting (sys_auth_setting, one row). Defaults follow login.md "설정".</summary>
public sealed class AuthSetting
{
    public int MaxFailedAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 60;

    /// <summary>Shows the remaining attempts on AUTH-002. It reveals that the ID exists.</summary>
    public bool ShowRemainingAttempts { get; init; }

    public bool AutoLoginEnabled { get; init; }

    public int AutoLoginValidDays { get; init; } = 14;

    public int SessionIdleMinutes { get; init; } = 30;

    public bool AllowConcurrentLogin { get; init; } = true;

    public int PasswordMinLength { get; init; } = 8;

    public int PasswordRequiredCategories { get; init; } = 3;

    /// <summary>0 means passwords never expire.</summary>
    public int PasswordExpireDays { get; init; }

    public int PasswordHistoryCount { get; init; } = 3;

    public string PasswordResetMode { get; init; } = "ADMIN_ONLY";
}

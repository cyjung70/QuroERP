namespace QuroErp.Contracts.Common.Auth;

/// <summary>Security setting routes (sys_auth_setting, login.md "설정", BC-UC-007).</summary>
public static class AuthSettingRoutes
{
    public const string Base = "api/sys/auth-settings";
}

/// <summary>Security settings (BC-BR-309). PUT sends the updatedAt it read.</summary>
public sealed record AuthSettingDto(
    int MaxFailedAttempts, int LockoutMinutes, bool ShowRemainingAttempts, bool AutoLoginEnabled, int AutoLoginValidDays,
    int SessionIdleMinutes, bool AllowConcurrentLogin, int PasswordMinLength, int PasswordRequiredCategories, int PasswordExpireDays,
    int PasswordHistoryCount, string PasswordResetMode, DateTimeOffset UpdatedAt);

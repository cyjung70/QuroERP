using System.Data;
using Dapper;
using QuroErp.Application.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Infrastructure.Common.Auth;

internal sealed class AuthSettingRepository : IAuthSettingRepository
{
    private const string Select = """
        SELECT max_failed_attempts, lockout_minutes, show_remaining_attempts, auto_login_enabled, auto_login_valid_days,
               session_idle_minutes, allow_concurrent_login, password_min_length, password_required_categories,
               password_expire_days, password_history_count, password_reset_mode
        FROM sys_auth_setting
        """;

    private const string SelectForEdit = """
        SELECT max_failed_attempts, lockout_minutes, show_remaining_attempts, auto_login_enabled, auto_login_valid_days,
               session_idle_minutes, allow_concurrent_login, password_min_length, password_required_categories,
               password_expire_days, password_history_count, password_reset_mode, updated_at
        FROM sys_auth_setting
        """;

    private const string Update = """
        UPDATE sys_auth_setting
        SET max_failed_attempts = @MaxFailedAttempts, lockout_minutes = @LockoutMinutes, show_remaining_attempts = @ShowRemainingAttempts,
            auto_login_enabled = @AutoLoginEnabled, auto_login_valid_days = @AutoLoginValidDays, session_idle_minutes = @SessionIdleMinutes,
            allow_concurrent_login = @AllowConcurrentLogin, password_min_length = @PasswordMinLength,
            password_required_categories = @PasswordRequiredCategories, password_expire_days = @PasswordExpireDays,
            password_history_count = @PasswordHistoryCount, password_reset_mode = @PasswordResetMode,
            updated_at = now(), updated_by = @UpdatedBy
        WHERE updated_at = @ExpectedUpdatedAt
        """;

    public Task<AuthSetting?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<AuthSetting>(
            new CommandDefinition(Select, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<AuthSettingRow?> FindForEditAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<EditRow>(new CommandDefinition(
            forUpdate ? SelectForEdit + " FOR UPDATE" : SelectForEdit, transaction: transaction, cancellationToken: cancellationToken));
        return row is null ? null : new AuthSettingRow(row.ToSetting(), new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc)));
    }

    public async Task<bool> UpdateAsync(
        AuthSetting setting, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                setting.MaxFailedAttempts, setting.LockoutMinutes, setting.ShowRemainingAttempts, setting.AutoLoginEnabled,
                setting.AutoLoginValidDays, setting.SessionIdleMinutes, setting.AllowConcurrentLogin, setting.PasswordMinLength,
                setting.PasswordRequiredCategories, setting.PasswordExpireDays, setting.PasswordHistoryCount, setting.PasswordResetMode,
                UpdatedBy = updatedBy, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    // Npgsql reads timestamptz as a UTC DateTime
    private sealed class EditRow
    {
        public int MaxFailedAttempts { get; init; }
        public int LockoutMinutes { get; init; }
        public bool ShowRemainingAttempts { get; init; }
        public bool AutoLoginEnabled { get; init; }
        public int AutoLoginValidDays { get; init; }
        public int SessionIdleMinutes { get; init; }
        public bool AllowConcurrentLogin { get; init; }
        public int PasswordMinLength { get; init; }
        public int PasswordRequiredCategories { get; init; }
        public int PasswordExpireDays { get; init; }
        public int PasswordHistoryCount { get; init; }
        public string PasswordResetMode { get; init; } = "";
        public DateTime UpdatedAt { get; init; }

        public AuthSetting ToSetting() => new()
        {
            MaxFailedAttempts = MaxFailedAttempts,
            LockoutMinutes = LockoutMinutes,
            ShowRemainingAttempts = ShowRemainingAttempts,
            AutoLoginEnabled = AutoLoginEnabled,
            AutoLoginValidDays = AutoLoginValidDays,
            SessionIdleMinutes = SessionIdleMinutes,
            AllowConcurrentLogin = AllowConcurrentLogin,
            PasswordMinLength = PasswordMinLength,
            PasswordRequiredCategories = PasswordRequiredCategories,
            PasswordExpireDays = PasswordExpireDays,
            PasswordHistoryCount = PasswordHistoryCount,
            PasswordResetMode = PasswordResetMode,
        };
    }
}

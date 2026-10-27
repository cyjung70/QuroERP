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

    public Task<AuthSetting?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<AuthSetting>(
            new CommandDefinition(Select, transaction: transaction, cancellationToken: cancellationToken));
}

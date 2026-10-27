using System.Data;
using Dapper;
using QuroErp.Application.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Infrastructure.Common.Auth;

internal sealed class UserLoginLogRepository : IUserLoginLogRepository
{
    /// <summary>로그인 이력은 시스템이 쓴다. 감사 컬럼 작성자.</summary>
    public const string AuditUser = "system";

    private const string Insert = """
        INSERT INTO sys_user_login_log (log_dt, app_user_id, login_userid, ip_addr, success, event_type, result_code, user_agent,
                                        created_by, updated_by)
        VALUES (@LogDt, @AppUserId, @LoginUserId, @IpAddr, @Success, @EventType, @ResultCode, @UserAgent, @AuditUser, @AuditUser)
        """;

    public Task InsertAsync(UserLoginLog log, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert,
            new
            {
                log.LogDt, log.AppUserId, log.LoginUserId, IpAddr = NullIfEmpty(log.IpAddr), log.Success, log.EventType, log.ResultCode,
                UserAgent = NullIfEmpty(log.UserAgent), AuditUser,
            },
            transaction,
            cancellationToken: cancellationToken));

    // database.md: 빈 문자열은 NULL로 저장한다
    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

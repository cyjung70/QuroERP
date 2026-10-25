using System.Data;
using Dapper;
using QuroErp.Application.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Infrastructure.Common.Auth;

internal sealed class AppUserRepository : IAppUserRepository
{
    private const string SelectUser = """
        SELECT u.id, u.userid AS user_id, u.password_hash, u.must_change_password, u.username AS user_name,
               r.role_code, u.valid_flag, u.user_valid_dt, u.password_valid_dt
        FROM sys_app_user u
        LEFT JOIN sys_role r ON r.id = u.role_id
        """;

    private const string SelectByUserId = SelectUser + " WHERE u.userid = @UserId";
    private const string SelectById = SelectUser + " WHERE u.id = @Id";

    public Task<AppUser?> FindByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<AppUser>(
            new CommandDefinition(SelectByUserId, new { UserId = userId }, transaction, cancellationToken: cancellationToken));

    public Task<AppUser?> FindByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<AppUser>(
            new CommandDefinition(SelectById, new { Id = id }, transaction, cancellationToken: cancellationToken));
}

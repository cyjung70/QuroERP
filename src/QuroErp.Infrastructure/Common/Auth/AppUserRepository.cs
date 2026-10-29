using System.Data;
using Dapper;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Infrastructure.Common.Auth;

internal sealed class AppUserRepository : IAppUserRepository
{
    private const string SelectUser = """
        SELECT u.id, u.userid AS user_id, u.password_hash, u.must_change_password, u.username AS user_name,
               r.role_code, u.valid_flag, u.user_valid_dt, u.password_valid_dt, u.lang_code,
               u.password_changed_at, u.default_warehouse_id, u.failed_count, u.locked_until, u.last_login_at, u.last_login_ip,
               u.token_version
        FROM sys_app_user u
        LEFT JOIN sys_role r ON r.id = u.role_id
        """;

    private const string SelectByUserId = SelectUser + " WHERE u.userid = @UserId";
    private const string SelectById = SelectUser + " WHERE u.id = @Id";

    // FOR UPDATE OF u: lock only the user row, not the role row
    private const string LockByUserId = SelectByUserId + " FOR UPDATE OF u";
    private const string LockById = SelectById + " FOR UPDATE OF u";

    private const string UpdateLoginState = """
        UPDATE sys_app_user
        SET failed_count = @FailedCount, locked_until = @LockedUntil, last_login_at = @LastLoginAt, last_login_ip = @LastLoginIp,
            lang_code = COALESCE(@LangCode, lang_code), updated_at = now(), updated_by = @AuditUser
        WHERE id = @Id
        """;

    private const string UpdatePassword = """
        UPDATE sys_app_user
        SET password_hash = @PasswordHash, password_changed_at = @Now, must_change_password = false, password_valid_dt = NULL,
            token_version = token_version + 1, updated_at = @Now, updated_by = @AuditUser
        WHERE id = @Id
        """;

    private const string SelectMenus = """
        SELECT m.menu_code, m.name, m.program_id, m.module, p.menu_code AS parent_menu_code, rm.full_access
        FROM sys_app_user u
        JOIN sys_role_menu rm ON rm.role_id = u.role_id
        JOIN sys_menu m ON m.id = rm.menu_id
        LEFT JOIN sys_menu p ON p.id = m.parent_menu_id
        WHERE u.id = @Id AND m.valid_flag = @ValidFlag
        ORDER BY m.menu_code
        """;

    private const string SelectDefaultWarehouse = """
        SELECT w.id, w.warehouse_code, w.warehouse_name
        FROM sys_app_user u
        JOIN bc_warehouse w ON w.id = u.default_warehouse_id OR (u.default_warehouse_id IS NULL AND w.is_default)
        WHERE u.id = @Id
        """;

    public Task<AppUser?> FindByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        QueryUserAsync(SelectByUserId, new { UserId = userId }, transaction, cancellationToken);

    public Task<AppUser?> FindByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        QueryUserAsync(SelectById, new { Id = id }, transaction, cancellationToken);

    public Task<AppUser?> LockByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        QueryUserAsync(LockByUserId, new { UserId = userId }, transaction, cancellationToken);

    public Task<AppUser?> LockByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        QueryUserAsync(LockById, new { Id = id }, transaction, cancellationToken);

    public Task SaveLoginStateAsync(AppUser user, string? langCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateLoginState,
            new
            {
                user.Id, user.FailedCount, user.LockedUntil, user.LastLoginAt, LastLoginIp = NullIfEmpty(user.LastLoginIp),
                LangCode = langCode, AuditUser = UserLoginLogRepository.AuditUser,
            },
            transaction,
            cancellationToken: cancellationToken));

    public Task ChangePasswordAsync(
        long id, string passwordHash, DateTimeOffset now, string auditUser, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdatePassword, new { Id = id, PasswordHash = passwordHash, Now = now, AuditUser = auditUser }, transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<AuthMenu>> FindMenusAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<AuthMenu>(new CommandDefinition(
            SelectMenus, new { Id = appUserId, ValidFlag = AppUser.ValidFlagUsed }, transaction, cancellationToken: cancellationToken))).AsList();

    public Task<AuthWarehouse?> FindDefaultWarehouseAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<AuthWarehouse>(
            new CommandDefinition(SelectDefaultWarehouse, new { Id = appUserId }, transaction, cancellationToken: cancellationToken));

    private static Task<AppUser?> QueryUserAsync(string sql, object parameters, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<AppUser>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));

    // database.md: 빈 문자열은 NULL로 저장한다
    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

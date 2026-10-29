using System.Data;
using Dapper;
using QuroErp.Application.Bc.Users;
using QuroErp.Domain.Bc.Users;

namespace QuroErp.Infrastructure.Bc.Users;

internal sealed class UserRepository : IUserRepository
{
    // No password_hash: the list and the screen never carry it (BC-BR-132)
    private const string Columns = """
        u.id, u.userid AS user_id, u.username AS user_name, u.role_id, r.role_code, u.email, u.phone, u.lang_code, u.user_valid_dt,
        u.valid_flag, u.must_change_password, u.failed_count, u.locked_until, u.last_login_at, u.last_login_ip, u.updated_at
        """;

    private const string From = " FROM sys_app_user u LEFT JOIN sys_role r ON r.id = u.role_id";

    private const string SelectById = "SELECT " + Columns + From + " WHERE u.id = @Id";

    // FOR UPDATE OF u: lock only the user row, not the role row
    private const string SelectByIdForUpdate = SelectById + " FOR UPDATE OF u";

    private const string SelectNext = "SELECT " + Columns + From + """

        WHERE u.userid > (SELECT userid FROM sys_app_user WHERE id = @Id)
        ORDER BY u.userid
        LIMIT 1
        """;

    private const string SelectPrevious = "SELECT " + Columns + From + """

        WHERE u.userid < (SELECT userid FROM sys_app_user WHERE id = @Id)
        ORDER BY u.userid DESC
        LIMIT 1
        """;

    private const string SearchCondition = """

        WHERE (@UserIdPattern IS NULL OR u.userid ILIKE @UserIdPattern ESCAPE '\')
          AND (@NamePattern IS NULL OR u.username ILIKE @NamePattern ESCAPE '\')
          AND (@RoleId IS NULL OR u.role_id = @RoleId)
          AND (@ValidFlag IS NULL OR u.valid_flag = @ValidFlag)
        """;

    private const string Search = "SELECT " + Columns + From + SearchCondition + """

        ORDER BY u.userid
        LIMIT @Limit OFFSET @Offset
        """;

    private const string Count = "SELECT count(*)" + From + SearchCondition;

    private const string SelectPasswordHash = "SELECT password_hash FROM sys_app_user WHERE id = @Id";

    private const string SelectUserIdExists = "SELECT EXISTS (SELECT 1 FROM sys_app_user WHERE userid = @UserId)";

    private const string SelectRoleInUse = "SELECT EXISTS (SELECT 1 FROM sys_role WHERE id = @Id AND use_yn)";

    // BC-BR-123: must change the password at sign-in; BC-BR-127: no per-user password expiry date
    private const string Insert = """
        INSERT INTO sys_app_user (userid, password_hash, must_change_password, username, role_id, email, phone, lang_code,
                                  user_valid_dt, valid_flag, password_valid_dt, created_by, updated_by)
        VALUES (@UserId, @PasswordHash, true, @UserName, @RoleId, @Email, @Phone, @LangCode,
                @UserValidDt, @ValidFlag, NULL, @CreatedBy, @CreatedBy)
        RETURNING id
        """;

    private const string Update = """
        UPDATE sys_app_user
        SET username = @UserName, role_id = @RoleId, email = @Email, phone = @Phone, lang_code = @LangCode,
            user_valid_dt = @UserValidDt, valid_flag = @ValidFlag,
            token_version = token_version + CASE WHEN @RaiseTokenVersion THEN 1 ELSE 0 END,
            updated_at = now(), updated_by = @UpdatedBy
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string Unlock = "UPDATE sys_app_user SET failed_count = 0, locked_until = NULL WHERE id = @Id";

    private const string ResetPassword = """
        UPDATE sys_app_user
        SET password_hash = @PasswordHash, must_change_password = true, password_changed_at = @Now, password_valid_dt = NULL,
            token_version = token_version + 1, updated_at = @Now, updated_by = @UpdatedBy
        WHERE id = @Id
        """;

    private const string RaiseTokenVersion = "UPDATE sys_app_user SET token_version = token_version + 1 WHERE id = @Id";

    public async Task<(IReadOnlyList<UserRow> Users, int Total)> SearchAsync(
        UserSearch search, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            UserIdPattern = string.IsNullOrWhiteSpace(search.UserId) ? null : EscapeLike(search.UserId.Trim()) + "%",
            NamePattern = string.IsNullOrWhiteSpace(search.UserName) ? null : "%" + EscapeLike(search.UserName.Trim()) + "%",
            search.RoleId,
            ValidFlag = string.IsNullOrWhiteSpace(search.ValidFlag) ? null : search.ValidFlag.Trim(),
            search.Limit,
            search.Offset,
        };
        var rows = await transaction.Connection!.QueryAsync<Row>(
            new CommandDefinition(Search, parameters, transaction, cancellationToken: cancellationToken));
        var total = await transaction.Connection!.ExecuteScalarAsync<int>(
            new CommandDefinition(Count, parameters, transaction, cancellationToken: cancellationToken));
        return (rows.Select(r => r.ToUserRow()).ToList(), total);
    }

    public async Task<UserRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forUpdate ? SelectByIdForUpdate : SelectById, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToUserRow();
    }

    public async Task<UserRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forward ? SelectNext : SelectPrevious, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return row?.ToUserRow();
    }

    public Task<string?> FindPasswordHashAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<string?>(
            new CommandDefinition(SelectPasswordHash, new { Id = id }, transaction, cancellationToken: cancellationToken));

    public Task<bool> UserIdExistsAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectUserIdExists, new { UserId = userId }, transaction, cancellationToken: cancellationToken));

    public Task<bool> IsRoleInUseAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectRoleInUse, new { Id = roleId }, transaction, cancellationToken: cancellationToken));

    public Task<long> InsertAsync(
        string userId, string passwordHash, UserAccountFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert,
            new
            {
                UserId = userId,
                PasswordHash = passwordHash,
                fields.UserName,
                fields.RoleId,
                fields.Email,
                fields.Phone,
                fields.LangCode,
                fields.UserValidDt,
                fields.ValidFlag,
                CreatedBy = createdBy,
            },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, UserAccountFields fields, bool raiseTokenVersion, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                Id = id,
                fields.UserName,
                fields.RoleId,
                fields.Email,
                fields.Phone,
                fields.LangCode,
                fields.UserValidDt,
                fields.ValidFlag,
                RaiseTokenVersion = raiseTokenVersion,
                UpdatedBy = updatedBy,
                ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public Task UnlockAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(Unlock, new { Id = id }, transaction, cancellationToken: cancellationToken));

    public Task ResetPasswordAsync(
        long id, string passwordHash, DateTimeOffset now, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            ResetPassword, new { Id = id, PasswordHash = passwordHash, Now = now, UpdatedBy = updatedBy }, transaction,
            cancellationToken: cancellationToken));

    public Task RaiseTokenVersionAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(
            new CommandDefinition(RaiseTokenVersion, new { Id = id }, transaction, cancellationToken: cancellationToken));

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id,
        string UserId,
        string? UserName,
        long? RoleId,
        string? RoleCode,
        string? Email,
        string? Phone,
        string? LangCode,
        DateOnly? UserValidDt,
        string? ValidFlag,
        bool MustChangePassword,
        int FailedCount,
        DateTime? LockedUntil,
        DateTime? LastLoginAt,
        string? LastLoginIp,
        DateTime UpdatedAt)
    {
        public UserRow ToUserRow() => new(
            Id, UserId, UserName, RoleId, RoleCode, Email, Phone, LangCode, UserValidDt, ValidFlag, MustChangePassword, FailedCount,
            Utc(LockedUntil), Utc(LastLoginAt), LastLoginIp, Utc(UpdatedAt)!.Value);

        private static DateTimeOffset? Utc(DateTime? value) =>
            value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
    }
}

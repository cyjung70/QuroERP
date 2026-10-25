using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace QuroERP.Database;

/// <summary>
/// 관리자 계정을 만들거나 비밀번호를 초기화한다 (security.md "비밀번호").
/// 비밀번호는 PasswordHasher(PBKDF2) 해시만 저장하고, 다음 로그인 때 변경을 강제한다.
/// </summary>
public static class AdminAccount
{
    public const string RoleCode = "admin";
    public const string AuditUser = "system";

    /// <returns>새로 만들었으면 true, 이미 있어서 비밀번호를 초기화했으면 false.</returns>
    public static async Task<bool> CreateOrResetAsync(string connectionString, string userId, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var passwordHash = new PasswordHasher<AdminUser>().HashPassword(new AdminUser(), password);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO sys_app_user (userid, password_hash, must_change_password, username, role_id, valid_flag, created_by, updated_by)
            SELECT @userid, @password_hash, true, @userid, r.id, 'Y', @audit_user, @audit_user
            FROM sys_role r
            WHERE r.role_code = @role_code
            ON CONFLICT (userid) DO UPDATE
               SET password_hash = EXCLUDED.password_hash,
                   must_change_password = true,
                   updated_at = now(),
                   updated_by = EXCLUDED.updated_by
            RETURNING (xmax = 0)
            """,
            connection);
        command.Parameters.AddWithValue("userid", userId);
        command.Parameters.AddWithValue("password_hash", passwordHash);
        command.Parameters.AddWithValue("audit_user", AuditUser);
        command.Parameters.AddWithValue("role_code", RoleCode);

        var inserted = await command.ExecuteScalarAsync(cancellationToken);
        return inserted switch
        {
            bool value => value,
            _ => throw new InvalidOperationException($"역할 '{RoleCode}'이 없다. 먼저 마이그레이션(Seed)을 적용한다."),
        };
    }

    /// <summary>PasswordHasher의 형식 인자. 해시 형식은 형식 인자와 관계없다.</summary>
    private sealed class AdminUser;
}

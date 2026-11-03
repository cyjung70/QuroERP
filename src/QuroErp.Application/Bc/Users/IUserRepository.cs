using System.Data;
using QuroErp.Domain.Bc.Users;

namespace QuroErp.Application.Bc.Users;

/// <summary>A sys_app_user row as user management shows it. No password hash (BC-BR-132).</summary>
public sealed record UserRow(
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
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastLoginAt,
    string? LastLoginIp,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

/// <param name="UserId">User ID starts with this (case-insensitive).</param>
/// <param name="UserName">User name contains this (case-insensitive).</param>
public sealed record UserSearch(string? UserId, string? UserName, long? RoleId, string? ValidFlag, int Offset, int Limit);

public interface IUserRepository
{
    /// <returns>The page in user ID order and the total count.</returns>
    Task<(IReadOnlyList<UserRow> Users, int Total)> SearchAsync(UserSearch search, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="forUpdate">Locks the user row. Every write path locks it before the user's tokens.</param>
    Task<UserRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The user before or after the given one in user ID order; null at either end.</summary>
    Task<UserRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The current password hash, for the reuse check and the history of a reset (BC-BR-129).</summary>
    Task<string?> FindPasswordHashAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<bool> UserIdExistsAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The role exists in sys_role and is in use (BC-BR-122).</summary>
    Task<bool> IsRoleInUseAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Inserts a user who must change the password at sign-in (BC-BR-123), without a password expiry date (BC-BR-127).</summary>
    /// <returns>The new id.</returns>
    Task<long> InsertAsync(
        string userId, string passwordHash, UserAccountFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves the fields when updated_at still equals <paramref name="expectedUpdatedAt"/>.</summary>
    /// <param name="raiseTokenVersion">The user's sessions must end at the next token refresh (BC-BR-125).</param>
    /// <returns>False when another save came first.</returns>
    Task<bool> UpdateAsync(
        long id, UserAccountFields fields, bool raiseTokenVersion, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// failed_count = 0, locked_until = null (BC-BR-128). Login state, not user data: updated_at stays, as after a login.
    /// </summary>
    Task UnlockAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>New password hash, must change at sign-in, token version + 1 (BC-BR-129).</summary>
    Task ResetPasswordAsync(
        long id, string passwordHash, DateTimeOffset now, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Token version + 1, so the user's sessions end at the next refresh (BC-BR-130). updated_at stays.</summary>
    Task RaiseTokenVersionAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);
}

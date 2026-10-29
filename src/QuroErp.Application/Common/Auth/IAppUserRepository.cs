using System.Data;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

public interface IAppUserRepository
{
    Task<AppUser?> FindByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<AppUser?> FindByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Finds and locks the user row so concurrent failures count correctly.</summary>
    Task<AppUser?> LockByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Finds and locks the user row.</summary>
    Task<AppUser?> LockByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves failed_count, locked_until, last_login_at and last_login_ip. updated_at and updated_by stay as they are.</summary>
    /// <param name="langCode">New lang_code (the language chosen on the login screen), or null to keep it.</param>
    Task SaveLoginStateAsync(AppUser user, string? langCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a new password hash: password_changed_at = now, must_change_password = false, password_valid_dt = NULL
    /// and token_version + 1 (BC-BR-058).
    /// </summary>
    /// <param name="auditUser">User ID written to updated_by.</param>
    Task ChangePasswordAsync(
        long id, string passwordHash, DateTimeOffset now, string auditUser, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Menus of the user's role, in menu code order.</summary>
    Task<IReadOnlyList<AuthMenu>> FindMenusAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The user's default warehouse, or the company default warehouse when the user has none.</summary>
    Task<AuthWarehouse?> FindDefaultWarehouseAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken);
}

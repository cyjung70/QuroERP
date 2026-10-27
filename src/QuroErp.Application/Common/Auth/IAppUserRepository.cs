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

    /// <summary>Saves failed_count, locked_until, last_login_at and last_login_ip.</summary>
    /// <param name="langCode">New lang_code (the language chosen on the login screen), or null to keep it.</param>
    Task SaveLoginStateAsync(AppUser user, string? langCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Menus of the user's role, in menu code order.</summary>
    Task<IReadOnlyList<AuthMenu>> FindMenusAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The user's default warehouse, or the company default warehouse when the user has none.</summary>
    Task<AuthWarehouse?> FindDefaultWarehouseAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken);
}

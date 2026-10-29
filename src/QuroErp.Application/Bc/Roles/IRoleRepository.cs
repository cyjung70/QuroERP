using System.Data;
using QuroErp.Contracts.Bc.Roles;

namespace QuroErp.Application.Bc.Roles;

/// <summary>Menu permission of one role: Full Access or read only (BC-BR-214).</summary>
public sealed record RoleMenuGrant(string MenuCode, bool FullAccess);

public interface IRoleRepository
{
    /// <summary>Roles in use (use_yn), in sort order, then role code order.</summary>
    Task<IReadOnlyList<RoleDto>> FindInUseAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The role if it is in use (BC-BR-211).</summary>
    Task<RoleDto?> FindInUseByIdAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The id of the role with this code, in use or not.</summary>
    Task<long?> FindIdByCodeAsync(string roleCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Menus assigned to the role, in menu code order.</summary>
    Task<IReadOnlyList<RoleMenuDto>> FindMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Menus not assigned to the role whose code starts with menuCode (all when null), in menu code order (BC-BR-212).</summary>
    Task<IReadOnlyList<MenuOptionDto>> FindUnassignedMenusAsync(
        long roleId, string? menuCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The ids among menuIds that exist in sys_menu.</summary>
    Task<IReadOnlySet<long>> FindExistingMenuIdsAsync(IReadOnlyCollection<long> menuIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the role menu screen's menu row (program id <c>/bc/role-menu</c>) so saves that can change who manages permissions run
    /// one at a time (BC-BR-216), and returns its id; null when that menu is not loaded.
    /// </summary>
    Task<long?> LockRoleMenuScreenAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Roles in use that have Full Access to the menu.</summary>
    Task<IReadOnlySet<long>> FindFullAccessRoleIdsAsync(long menuId, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertMenuAsync(long roleId, long menuId, bool fullAccess, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>False when the row is gone or its updated_at is not expectedUpdatedAt.</summary>
    Task<bool> UpdateMenuAsync(
        long roleId, long menuId, bool fullAccess, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>False when the row is gone or its updated_at is not expectedUpdatedAt.</summary>
    Task<bool> DeleteMenuAsync(long roleId, long menuId, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken);

    Task DeleteAllMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Menus in use (valid_flag Y) assigned to the role with this code, if the role is in use (BC-BR-206, BC-BR-214).</summary>
    Task<IReadOnlyList<RoleMenuGrant>> FindPermissionsAsync(string roleCode, IDbTransaction transaction, CancellationToken cancellationToken);
}

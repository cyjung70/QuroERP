using System.Data;
using QuroErp.Application.Bc.Roles;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Tests.Bc.Roles;

/// <summary>
/// sys_role, sys_menu and sys_role_menu in memory. Roles: admin (1, the signed-in kim), user (2), retired (3, not in use).
/// Menus: ROLEMENU (100, /bc/role-menu), ITEM (101, /bc/item), USER (102, /bc/user). admin has ROLEMENU and ITEM with Full Access,
/// user reads ITEM. As after the seed, admin also has Full Access to every screen path that guards an API (ScreenPaths.All).
/// </summary>
public sealed class InMemoryRoleRepository : IRoleRepository
{
    public const long AdminRoleId = 1;
    public const long UserRoleId = 2;
    public const long RetiredRoleId = 3;
    public const long RoleMenuId = 100;
    public const long ItemMenuId = 101;
    public const long UserMenuId = 102;

    private static readonly Dictionary<long, string> Paths = new() { [RoleMenuId] = "/bc/role-menu", [ItemMenuId] = "/bc/item", [UserMenuId] = "/bc/user" };

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();

    private readonly (long Id, string Code, bool InUse)[] _roles = [(AdminRoleId, "admin", true), (UserRoleId, "user", true), (RetiredRoleId, "retired", false)];

    private readonly MenuOptionDto[] _menus =
    [
        new(RoleMenuId, "ROLEMENU", "Role menu", "P", "bc"),
        new(ItemMenuId, "ITEM", "Item", "P", "bc"),
        new(UserMenuId, "USER", "User", "P", "bc"),
    ];

    public Dictionary<(long RoleId, long MenuId), (bool FullAccess, DateTimeOffset UpdatedAt)> Assignments { get; } = new()
    {
        [(AdminRoleId, RoleMenuId)] = (true, Start),
        [(AdminRoleId, ItemMenuId)] = (true, Start),
        [(UserRoleId, ItemMenuId)] = (false, Start),
    };

    public Task<IReadOnlyList<RoleDto>> FindInUseAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoleDto>>(_roles.Where(r => r.InUse).Select(r => new RoleDto(r.Id, r.Code)).ToList());

    public Task<RoleDto?> FindInUseByIdAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.Where(r => r.Id == roleId && r.InUse).Select(r => new RoleDto(r.Id, r.Code)).Cast<RoleDto?>().FirstOrDefault());

    public Task<long?> FindIdByCodeAsync(string roleCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.Where(r => r.Code == roleCode).Select(r => (long?)r.Id).FirstOrDefault());

    public Task<IReadOnlyList<RoleMenuDto>> FindMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<RoleMenuDto>>(_menus
                .Where(m => Assignments.ContainsKey((roleId, m.MenuId)))
                .Select(m => (Menu: m, Row: Assignments[(roleId, m.MenuId)]))
                .Select(x => new RoleMenuDto(x.Menu.MenuId, x.Menu.MenuCode, x.Menu.Name, x.Menu.Type, x.Menu.Module, x.Row.FullAccess, x.Row.UpdatedAt))
                .OrderBy(m => m.MenuCode, StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task<IReadOnlyList<MenuOptionDto>> FindUnassignedMenusAsync(
        long roleId, string? menuCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MenuOptionDto>>(_menus
                .Where(m => !Assignments.ContainsKey((roleId, m.MenuId)))
                .Where(m => menuCode is null || m.MenuCode.StartsWith(menuCode, StringComparison.Ordinal))
                .OrderBy(m => m.MenuCode, StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task<IReadOnlySet<long>> FindExistingMenuIdsAsync(
        IReadOnlyCollection<long> menuIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(_menus.Select(m => m.MenuId).Intersect(menuIds).ToHashSet());

    public Task<long?> LockRoleMenuScreenAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<long?>(RoleMenuId);

    public Task<IReadOnlySet<long>> FindFullAccessRoleIdsAsync(long menuId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlySet<long>>(Assignments
                .Where(a => a.Key.MenuId == menuId && a.Value.FullAccess && _roles.Any(r => r.Id == a.Key.RoleId && r.InUse))
                .Select(a => a.Key.RoleId).ToHashSet());
        }
    }

    public Task InsertMenuAsync(
        long roleId, long menuId, bool fullAccess, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Assignments.Add((roleId, menuId), (fullAccess, Start.AddMinutes(1)));
        }
        return Task.CompletedTask;
    }

    public Task<bool> UpdateMenuAsync(
        long roleId, long menuId, bool fullAccess, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!Assignments.TryGetValue((roleId, menuId), out var row) || row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            Assignments[(roleId, menuId)] = (fullAccess, row.UpdatedAt.AddMinutes(1));
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteMenuAsync(
        long roleId, long menuId, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(Assignments.TryGetValue((roleId, menuId), out var row) && row.UpdatedAt == expectedUpdatedAt
                && Assignments.Remove((roleId, menuId)));
        }
    }

    public Task DeleteAllMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var key in Assignments.Keys.Where(k => k.RoleId == roleId).ToList())
            {
                Assignments.Remove(key);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RoleMenuGrant>> FindPermissionsAsync(string roleCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var role = _roles.FirstOrDefault(r => r.Code == roleCode && r.InUse);
            var grants = _menus
                .Where(m => Assignments.ContainsKey((role.Id, m.MenuId)))
                .Select(m => new RoleMenuGrant(Paths[m.MenuId], Assignments[(role.Id, m.MenuId)].FullAccess));
            var seeded = role.Id == AdminRoleId ? ScreenPaths.All.Select(p => new RoleMenuGrant(p, true)) : [];
            return Task.FromResult<IReadOnlyList<RoleMenuGrant>>([.. grants, .. seeded]);
        }
    }
}

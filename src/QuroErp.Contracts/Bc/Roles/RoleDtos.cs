namespace QuroErp.Contracts.Bc.Roles;

/// <summary>Role API routes (BC-UC-005 role menu permissions).</summary>
public static class RoleRoutes
{
    /// <summary>GET: roles in use, for any signed-in user (role select fields such as user management).</summary>
    public const string Base = "api/sys/roles";

    /// <summary>
    /// {Base}/{roleId}/menus — GET the menus assigned to the role, PUT the grid changes (one transaction),
    /// DELETE every assignment of the role (BC-BR-213, 216).
    /// </summary>
    public const string Menus = "menus";

    /// <summary>GET {Base}/{roleId}/unassigned-menus?menuCode=: menus the role does not have yet, for the add dialog (BC-BR-212).</summary>
    public const string UnassignedMenus = "unassigned-menus";
}

/// <summary>One role in use.</summary>
public sealed record RoleDto(long Id, string RoleCode);

/// <summary>A menu assigned to a role. Without Full Access the role may only read the screen (BC-BR-214).</summary>
/// <param name="Type">P program, U popup, M menu group (BC-BR-203).</param>
public sealed record RoleMenuDto(
    long MenuId, string MenuCode, string? Name, string? Type, string? Module, bool FullAccess, DateTimeOffset UpdatedAt);

/// <summary>A menu that can still be added to the role.</summary>
public sealed record MenuOptionDto(long MenuId, string MenuCode, string? Name, string? Type, string? Module);

/// <summary>Grid changes of one role, saved together or not at all.</summary>
public sealed record SaveRoleMenusRequest(
    IReadOnlyList<RoleMenuInsert>? Inserted, IReadOnlyList<RoleMenuUpdate>? Updated, IReadOnlyList<RoleMenuDelete>? Deleted);

public sealed record RoleMenuInsert(long MenuId, bool FullAccess);

/// <summary>Full Access change of an assigned menu (BC-BR-213). updatedAt is the row's value when it was read (409 when stale).</summary>
public sealed record RoleMenuUpdate(long MenuId, bool FullAccess, DateTimeOffset UpdatedAt);

public sealed record RoleMenuDelete(long MenuId, DateTimeOffset UpdatedAt);

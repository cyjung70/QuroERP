namespace QuroErp.Contracts.Bc.Menus;

/// <summary>Company menu API routes (BC-UC-006).</summary>
public static class MenuRoutes
{
    /// <summary>GET ?menuCode= (code prefix, code order), PUT the grid changes in one transaction (BC-BR-201~207).</summary>
    public const string Base = "api/sys/menus";
}

/// <summary>A menu (sys_menu). The parent is shown and saved by its menu code.</summary>
/// <param name="Type">P program, U popup, M menu group (BC-BR-203).</param>
/// <param name="Module">bc, mm, sd, fi or rpt (BC-BR-204).</param>
/// <param name="ValidFlag">VALID_FLAG code. A menu not in use leaves menus and permissions (BC-BR-206).</param>
public sealed record MenuDto(
    long Id, string MenuCode, string? Name, string? Type, string? ProgramId, string? Module, string? ParentMenuCode, string? ValidFlag,
    DateTimeOffset UpdatedAt);

/// <summary>Grid changes, saved together or not at all.</summary>
public sealed record SaveMenusRequest(IReadOnlyList<MenuInsert>? Inserted, IReadOnlyList<MenuUpdate>? Updated, IReadOnlyList<MenuDelete>? Deleted);

/// <summary>A new menu. Its parent may be another menu added in the same save.</summary>
public sealed record MenuInsert(
    string? MenuCode, string? Name, string? Type, string? ProgramId, string? Module, string? ParentMenuCode, string? ValidFlag);

/// <summary>A changed menu. The menu code cannot change (BC-BR-201). updatedAt is the row's value when it was read (409 when stale).</summary>
public sealed record MenuUpdate(
    long Id, string? Name, string? Type, string? ProgramId, string? Module, string? ParentMenuCode, string? ValidFlag, DateTimeOffset UpdatedAt);

public sealed record MenuDelete(long Id, DateTimeOffset UpdatedAt);

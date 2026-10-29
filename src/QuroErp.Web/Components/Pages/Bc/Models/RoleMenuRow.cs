using QuroErp.Contracts.Bc.Roles;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>
/// A grid row on the role menu permission screen (BC-UC-005). Only Full Access is edited; the menu comes from the add dialog
/// (BC-BR-212), so a new row arrives filled.
/// </summary>
public sealed class RoleMenuRow
{
    public long MenuId { get; set; }
    public string MenuCode { get; set; } = "";
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool FullAccess { get; set; }

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public static RoleMenuRow From(RoleMenuDto dto) => new()
    {
        MenuId = dto.MenuId,
        MenuCode = dto.MenuCode,
        Name = dto.Name,
        Type = dto.Type,
        FullAccess = dto.FullAccess,
        UpdatedAt = dto.UpdatedAt,
    };

    /// <summary>A menu picked in the add dialog. It starts as read only, like a new assignment in AS IS. [추정]</summary>
    public static RoleMenuRow From(MenuOptionDto option) => new()
    {
        MenuId = option.MenuId,
        MenuCode = option.MenuCode,
        Name = option.Name,
        Type = option.Type,
    };

    /// <summary>Grid changes as one save request (BC-BR-213). Loaded rows always have updatedAt.</summary>
    public static SaveRoleMenusRequest ToRequest(IReadOnlyList<RoleMenuRow> added, IReadOnlyList<RoleMenuRow> updated, IReadOnlyList<RoleMenuRow> deleted) =>
        new(
            [.. added.Select(r => new RoleMenuInsert(r.MenuId, r.FullAccess))],
            [.. updated.Select(r => new RoleMenuUpdate(r.MenuId, r.FullAccess, r.UpdatedAt!.Value))],
            [.. deleted.Select(r => new RoleMenuDelete(r.MenuId, r.UpdatedAt!.Value))]);
}

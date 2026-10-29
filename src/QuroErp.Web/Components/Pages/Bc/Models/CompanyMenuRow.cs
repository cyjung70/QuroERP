using QuroErp.Contracts.Bc.Menus;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>A grid row on the company menu screen (BC-UC-006). The menu code is entered only on a new row (BC-BR-201).</summary>
public sealed class CompanyMenuRow
{
    public long Id { get; set; }
    public string? MenuCode { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? ProgramId { get; set; }
    public string? Module { get; set; }
    public string? ParentMenuCode { get; set; }
    public string? ValidFlag { get; set; }

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>A new row: a program menu in use. [추정]</summary>
    public static CompanyMenuRow New() => new() { Type = "P", ValidFlag = "Y" };

    public static CompanyMenuRow From(MenuDto dto) => new()
    {
        Id = dto.Id,
        MenuCode = dto.MenuCode,
        Name = dto.Name,
        Type = dto.Type,
        ProgramId = dto.ProgramId,
        Module = dto.Module,
        ParentMenuCode = dto.ParentMenuCode,
        ValidFlag = dto.ValidFlag,
        UpdatedAt = dto.UpdatedAt,
    };

    /// <summary>Grid changes as one save request. Loaded rows always have updatedAt; the API normalizes and checks every value.</summary>
    public static SaveMenusRequest ToRequest(IReadOnlyList<CompanyMenuRow> added, IReadOnlyList<CompanyMenuRow> updated, IReadOnlyList<CompanyMenuRow> deleted) =>
        new(
            [.. added.Select(r => new MenuInsert(r.MenuCode ?? "", r.Name, r.Type, r.ProgramId, r.Module, r.ParentMenuCode, r.ValidFlag))],
            [.. updated.Select(r => new MenuUpdate(r.Id, r.Name, r.Type, r.ProgramId, r.Module, r.ParentMenuCode, r.ValidFlag, r.UpdatedAt!.Value))],
            [.. deleted.Select(r => new MenuDelete(r.Id, r.UpdatedAt!.Value))]);
}

using QuroErp.Application.Bc.Roles;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Menus;
using QuroErp.Domain.Bc.Menus;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Application.Bc.Menus;

/// <summary>Company menus (BC-UC-006, BC-BR-201~207).</summary>
public sealed class MenuService(
    IMenuRepository menus, ICodeValidator codes, MenuPermissionService permissions, IUnitOfWorkFactory unitOfWork)
{
    public async Task<IReadOnlyList<MenuDto>> SearchAsync(string? menuCode, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await menus.SearchAsync(
            string.IsNullOrWhiteSpace(menuCode) ? null : menuCode.Trim().ToUpperInvariant(), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    /// <summary>
    /// Saves added, changed and deleted rows in one transaction. One failing row saves nothing; its menu code is the message arg.
    /// Returns every menu after the save.
    /// </summary>
    public async Task<IReadOnlyList<MenuDto>> SaveAsync(SaveMenusRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        IReadOnlyList<MenuInsert> inserted = request.Inserted ?? [];
        IReadOnlyList<MenuUpdate> updated = request.Updated ?? [];
        IReadOnlyList<MenuDelete> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var stored = (await menus.LockAllAsync(uow.Transaction, cancellationToken)).ToDictionary(m => m.Id);
        var codeOf = stored.Values.ToDictionary(m => m.Id, m => m.MenuCode);

        // Rows the save names must still be there as they were read; otherwise someone else saved first
        foreach (var (id, updatedAt) in updated.Select(u => (u.Id, u.UpdatedAt)).Concat(deleted.Select(d => (d.Id, d.UpdatedAt))))
        {
            if (!stored.TryGetValue(id, out var row) || row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }

        var newRows = new List<(string Code, MenuFields Fields)>();
        foreach (var row in inserted)
        {
            var code = MenuRules.NormalizeCode(row.MenuCode);
            newRows.Add((code, MenuRules.Normalize(code, row.Name, row.Type, row.ProgramId, row.Module, row.ParentMenuCode, row.ValidFlag)));
        }
        var changedRows = updated.Select(row =>
        {
            var code = codeOf[row.Id];
            return (row.Id, Code: code, Fields: MenuRules.Normalize(code, row.Name, row.Type, row.ProgramId, row.Module, row.ParentMenuCode, row.ValidFlag));
        }).ToList();
        foreach (var fields in newRows.Select(r => r.Fields).Concat(changedRows.Select(r => r.Fields)))
        {
            await codes.EnsureValidAsync(CodeMajors.ValidFlag, fields.ValidFlag, cancellationToken);
        }

        // The tree the save leaves: stored menus, minus deleted, with changed rows replaced and new rows added
        var deletedIds = deleted.Select(d => d.Id).ToHashSet();
        var changedById = changedRows.ToDictionary(r => r.Id);
        var tree = stored.Values.Where(m => !deletedIds.Contains(m.Id)).Select(m => changedById.TryGetValue(m.Id, out var changed)
            ? new MenuNode(m.MenuCode, changed.Fields.Type, changed.Fields.ParentMenuCode)
            : new MenuNode(m.MenuCode, m.Type ?? "", m.ParentMenuId is { } parentId ? codeOf[parentId] : null)).ToList();
        foreach (var (code, fields) in newRows)
        {
            // BC-BR-201: unique among the menus the save leaves
            if (tree.Any(n => n.MenuCode == code))
            {
                throw new DomainException(MenuRules.InvalidCodeCode, code);
            }
            tree.Add(new MenuNode(code, fields.Type, fields.ParentMenuCode));
        }
        // A changed menu's children are checked too: a group changed to P or U can no longer hold them
        var changedCodes = newRows.Select(r => r.Code).Concat(changedRows.Select(r => r.Code)).ToHashSet(StringComparer.Ordinal);
        MenuRules.EnsureParents(
            tree, tree.Where(n => changedCodes.Contains(n.MenuCode) || n.ParentMenuCode is { } p && changedCodes.Contains(p)).Select(n => n.MenuCode));

        var assigned = await menus.FindAssignedAsync(deletedIds, uow.Transaction, cancellationToken);
        foreach (var id in deletedIds)
        {
            MenuRules.EnsureDeletable(codeOf[id], assigned.Contains(id), tree);
        }

        // Writes: deletes first (their codes may be reused), then new rows, then parents and changed rows by code.
        // A changed menu moving out of a deleted group is detached first; its update sets the new parent afterwards.
        foreach (var row in changedRows.Where(r => stored[r.Id].ParentMenuId is { } p && deletedIds.Contains(p)))
        {
            await menus.SetParentAsync(row.Id, null, uow.Transaction, cancellationToken);
        }
        await menus.DeleteAsync(deletedIds, uow.Transaction, cancellationToken);
        var idOf = stored.Values.Where(m => !deletedIds.Contains(m.Id)).ToDictionary(m => m.MenuCode, m => m.Id, StringComparer.Ordinal);
        foreach (var (code, fields) in newRows)
        {
            idOf[code] = await menus.InsertAsync(code, fields, updatedBy, uow.Transaction, cancellationToken);
        }
        foreach (var (code, fields) in newRows.Where(r => r.Fields.ParentMenuCode is not null))
        {
            await menus.SetParentAsync(idOf[code], idOf[fields.ParentMenuCode!], uow.Transaction, cancellationToken);
        }
        foreach (var row in changedRows)
        {
            var parentId = row.Fields.ParentMenuCode is { } parent ? idOf[parent] : (long?)null;
            if (!await menus.UpdateAsync(row.Id, row.Fields, parentId, stored[row.Id].UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }

        var after = await menus.SearchAsync(null, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        // BC-BR-206, BC-BR-215: a menu set not in use or deleted changes every role's permissions
        permissions.InvalidateAll();
        return after;
    }
}

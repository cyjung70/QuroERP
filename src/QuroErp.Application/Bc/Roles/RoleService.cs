using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Domain.Bc.Roles;

namespace QuroErp.Application.Bc.Roles;

/// <summary>Roles in use and their menu permissions (BC-UC-005, BC-BR-211~216).</summary>
public sealed class RoleService(IRoleRepository roles, MenuPermissionService permissions, IUnitOfWorkFactory unitOfWork)
{
    private const string NotFoundCode = "Common-E-006";

    public async Task<IReadOnlyList<RoleDto>> GetInUseAsync(CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var list = await roles.FindInUseAsync(uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<IReadOnlyList<RoleMenuDto>> GetMenusAsync(long roleId, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await RequireRoleAsync(roleId, uow, cancellationToken);
        var menus = await roles.FindMenusAsync(roleId, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return menus;
    }

    /// <summary>BC-BR-212: the add dialog shows only menus the role does not have yet.</summary>
    public async Task<IReadOnlyList<MenuOptionDto>> GetUnassignedMenusAsync(long roleId, string? menuCode, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        await RequireRoleAsync(roleId, uow, cancellationToken);
        var menus = await roles.FindUnassignedMenusAsync(
            roleId, string.IsNullOrWhiteSpace(menuCode) ? null : menuCode.Trim().ToUpperInvariant(), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return menus;
    }

    /// <summary>
    /// Saves added, Full Access-changed and deleted rows in one transaction (BC-BR-212, 213). One failing row saves nothing.
    /// </summary>
    /// <param name="myRoleCode">Role of the signed-in user, for BC-BR-216.</param>
    public async Task<IReadOnlyList<RoleMenuDto>> SaveMenusAsync(
        long roleId, SaveRoleMenusRequest request, string? myRoleCode, string updatedBy, CancellationToken cancellationToken)
    {
        IReadOnlyList<RoleMenuInsert> inserted = request.Inserted ?? [];
        IReadOnlyList<RoleMenuUpdate> updated = request.Updated ?? [];
        IReadOnlyList<RoleMenuDelete> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var role = await RequireRoleAsync(roleId, uow, cancellationToken);
        var guard = await BeginGuardAsync(myRoleCode, uow, cancellationToken);

        // Rows deleted in the same save free their menus for a new row
        var assigned = (await roles.FindMenusAsync(roleId, uow.Transaction, cancellationToken)).Select(m => m.MenuId)
            .Except(deleted.Select(d => d.MenuId)).ToHashSet();
        RoleMenuRules.EnsureNoDuplicates(assigned, inserted.Select(i => i.MenuId));
        var addedIds = inserted.Select(i => i.MenuId).ToList();
        var existing = await roles.FindExistingMenuIdsAsync(addedIds, uow.Transaction, cancellationToken);
        // Menu ids are identity values from 1, so 0 means every added menu exists
        if (addedIds.Find(id => !existing.Contains(id)) is var unknown and not 0)
        {
            throw new NotFoundException(NotFoundCode, unknown);
        }

        foreach (var row in deleted)
        {
            if (!await roles.DeleteMenuAsync(roleId, row.MenuId, row.UpdatedAt, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }
        foreach (var row in updated)
        {
            if (!await roles.UpdateMenuAsync(roleId, row.MenuId, row.FullAccess, row.UpdatedAt, updatedBy, uow.Transaction, cancellationToken))
            {
                throw new ConcurrencyException();
            }
        }
        foreach (var row in inserted)
        {
            await roles.InsertMenuAsync(roleId, row.MenuId, row.FullAccess, updatedBy, uow.Transaction, cancellationToken);
        }

        await CheckGuardAsync(guard, uow, cancellationToken);
        var menus = await roles.FindMenusAsync(roleId, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        permissions.Invalidate(role.RoleCode);
        return menus;
    }

    /// <summary>Deletes every assignment of the role (BC-BR-213), unless that breaks BC-BR-216.</summary>
    public async Task DeleteMenusAsync(long roleId, string? myRoleCode, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var role = await RequireRoleAsync(roleId, uow, cancellationToken);
        var guard = await BeginGuardAsync(myRoleCode, uow, cancellationToken);
        await roles.DeleteAllMenusAsync(roleId, uow.Transaction, cancellationToken);
        await CheckGuardAsync(guard, uow, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        permissions.Invalidate(role.RoleCode);
    }

    private async Task<RoleDto> RequireRoleAsync(long roleId, IUnitOfWork uow, CancellationToken cancellationToken) =>
        await roles.FindInUseByIdAsync(roleId, uow.Transaction, cancellationToken)
            ?? throw new BadRequestException(RoleMenuRules.NoRoleCode);

    // BC-BR-216: who has Full Access to the role menu screen before the save, checked again after the writes
    private async Task<AccessGuard?> BeginGuardAsync(string? myRoleCode, IUnitOfWork uow, CancellationToken cancellationToken)
    {
        if (await roles.LockRoleMenuScreenAsync(uow.Transaction, cancellationToken) is not { } menuId)
        {
            return null;
        }
        var before = await roles.FindFullAccessRoleIdsAsync(menuId, uow.Transaction, cancellationToken);
        var myRoleId = myRoleCode is null ? null : await roles.FindIdByCodeAsync(myRoleCode, uow.Transaction, cancellationToken);
        return new AccessGuard(menuId, before, myRoleId);
    }

    private async Task CheckGuardAsync(AccessGuard? guard, IUnitOfWork uow, CancellationToken cancellationToken)
    {
        if (guard is null)
        {
            return;
        }
        var after = await roles.FindFullAccessRoleIdsAsync(guard.MenuId, uow.Transaction, cancellationToken);
        RoleMenuRules.EnsureRoleMenuAccessKept(guard.Before, after, guard.MyRoleId);
    }

    private sealed record AccessGuard(long MenuId, IReadOnlySet<long> Before, long? MyRoleId);
}

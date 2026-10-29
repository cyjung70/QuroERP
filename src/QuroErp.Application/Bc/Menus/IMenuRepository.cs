using System.Data;
using QuroErp.Contracts.Bc.Menus;
using QuroErp.Domain.Bc.Menus;

namespace QuroErp.Application.Bc.Menus;

/// <summary>A stored menu with what the tree checks need.</summary>
public sealed record MenuRecord(long Id, string MenuCode, string? Type, long? ParentMenuId, DateTimeOffset UpdatedAt);

public interface IMenuRepository
{
    /// <summary>Menus whose code starts with menuCode (all when null), in menu code order.</summary>
    Task<IReadOnlyList<MenuDto>> SearchAsync(string? menuCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Every menu, locked FOR UPDATE so tree changes run one at a time (BC-BR-205).</summary>
    Task<IReadOnlyList<MenuRecord>> LockAllAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The ids among menuIds that are assigned to any role (BC-BR-207).</summary>
    Task<IReadOnlySet<long>> FindAssignedAsync(IReadOnlyCollection<long> menuIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Inserts a menu without its parent; the parent is set afterwards because it may be new too.</summary>
    Task<long> InsertAsync(string menuCode, MenuFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>False when the row is gone or its updated_at is not expectedUpdatedAt.</summary>
    Task<bool> UpdateAsync(
        long id, MenuFields fields, long? parentMenuId, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken);

    Task SetParentAsync(long id, long? parentMenuId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the menus; they have no children left and no role assignments.</summary>
    Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);
}

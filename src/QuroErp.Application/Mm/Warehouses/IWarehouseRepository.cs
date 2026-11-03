using System.Data;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Mm.Warehouses;

namespace QuroErp.Application.Mm.Warehouses;

/// <summary>A stored bc_warehouse row.</summary>
public sealed record WarehouseRow(
    long Id, string WarehouseCode, string WarehouseName, bool IsDefault, bool UseYn, string WarehouseType, string? Remarks,
    DateTimeOffset UpdatedAt)
{
    public WarehouseState ToState() => new(Id, WarehouseCode, WarehouseName, IsDefault, UseYn, WarehouseType, Remarks);
}

public interface IWarehouseRepository
{
    /// <summary>
    /// Warehouses in use (use_yn), in warehouse code order. The consignment warehouse only with includeConsignment (MM-BR-710),
    /// warehouses not in use only with includeInactive.
    /// </summary>
    Task<IReadOnlyList<WarehouseOptionDto>> FindOptionsAsync(
        bool includeConsignment, bool includeInactive, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The consignment warehouse (MM-BR-709), null when the database has none.</summary>
    Task<ConsignmentWarehouse?> FindConsignmentAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Every warehouse in code order. forUpdate locks them all (FOR UPDATE), so batch saves run one at a time.</summary>
    Task<IReadOnlyList<WarehouseRow>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// MM-BR-704: of the warehouses, those that cannot be set not in use, with the reason: an item balance other than 0
    /// (mm_onhand) or a stock movement dated after today.
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> FindInactivationBlockersAsync(
        IReadOnlyCollection<long> warehouseIds, DateOnly today, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// MM-BR-704: of the warehouses, those that cannot be deleted, with the reason: a stock movement, or a document, stock row
    /// or stock count that refers to it. User default warehouses do not block; <see cref="DeleteAsync"/> clears them.
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> FindDeleteBlockersAsync(
        IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Switches the stored default warehouse off (MM-BR-702), before another one becomes the default.</summary>
    Task ClearDefaultAsync(string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<long> InsertAsync(WarehouseState warehouse, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves name, default, use and remarks of a row locked by <see cref="FindAllAsync"/>. Code and type never change.</summary>
    Task UpdateAsync(WarehouseState warehouse, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the warehouses. Users who had one as their default warehouse get none, so new documents use the company
    /// default warehouse (MM-BR-703).
    /// </summary>
    Task DeleteAsync(IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken);
}

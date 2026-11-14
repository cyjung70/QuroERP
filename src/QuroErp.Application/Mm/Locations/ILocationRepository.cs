using System.Data;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Domain.Mm.Locations;

namespace QuroErp.Application.Mm.Locations;

/// <summary>A stored bc_location row with the balance on it.</summary>
public sealed record LocationRow(
    long Id, long WarehouseId, string LocationCode, string? LocationName, bool IsSystem, bool UseYn, string? Remarks, decimal OnhandQty,
    DateTimeOffset UpdatedAt)
{
    public LocationState ToState() => new(Id, WarehouseId, LocationCode, LocationName, IsSystem, UseYn, Remarks);
}

public interface ILocationRepository
{
    /// <summary>The warehouse exists.</summary>
    Task<bool> WarehouseExistsAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Locations of the warehouses, '*' first and then in code order. code keeps codes that start with it (ignoring case);
    /// Locations not in use only with includeInactive.
    /// </summary>
    Task<IReadOnlyList<LocationRow>> FindAsync(
        long warehouseId, string? code, bool includeInactive, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Every Location of the warehouses that have one of the ids or are named, locked (FOR UPDATE) in id order, so batch saves on
    /// one warehouse run one at a time.
    /// </summary>
    Task<IReadOnlyList<LocationRow>> LockAsync(
        IReadOnlyCollection<long> locationIds, IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Locations in use of the warehouse, '*' first and then in code order (MM-BR-717).</summary>
    Task<IReadOnlyList<LocationOptionDto>> FindOptionsAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>MM-BR-717: of the Locations, those with an item balance other than 0, with the reason ONHAND.</summary>
    Task<IReadOnlyDictionary<long, string>> FindInactivationBlockersAsync(
        IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// MM-BR-717: of the Locations, those that cannot be deleted: a balance other than 0 (ONHAND), or stock movements or stock rows
    /// that refer to them (HAS_MOVEMENT).
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> FindDeleteBlockersAsync(
        IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertAsync(LocationState location, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves name, use and remarks of a locked row. Code, warehouse and the system flag never change.</summary>
    Task UpdateAsync(LocationState location, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken);
}

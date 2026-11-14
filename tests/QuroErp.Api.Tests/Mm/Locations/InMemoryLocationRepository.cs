using System.Data;
using QuroErp.Application.Mm.Locations;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Domain.Mm.Locations;

namespace QuroErp.Api.Tests.Mm.Locations;

/// <summary>
/// bc_location in memory: warehouse 1 has '*' (id 1), A-01 with a balance of 5 (id 2) and B-01 not in use (id 3); warehouse 2
/// has only '*' (id 4). A-01 has a balance, so it cannot be set not in use or deleted.
/// </summary>
public sealed class InMemoryLocationRepository : ILocationRepository
{
    public static readonly DateTimeOffset Stored = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly List<LocationRow> _rows =
    [
        new(1, 1, "*", null, true, true, null, 0, Stored),
        new(2, 1, "A-01", "Shelf A1", false, true, null, 5, Stored),
        new(3, 1, "B-01", null, false, false, null, 0, Stored),
        new(4, 2, "*", null, true, true, null, 0, Stored),
    ];

    private long _nextId = 5;

    public Task<bool> WarehouseExistsAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(warehouseId is 1 or 2);

    public Task<IReadOnlyList<LocationRow>> FindAsync(
        long warehouseId, string? code, bool includeInactive, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Where(l => l.WarehouseId == warehouseId && (includeInactive || l.UseYn)
            && (string.IsNullOrEmpty(code) || l.LocationCode.StartsWith(code, StringComparison.OrdinalIgnoreCase))));

    public Task<IReadOnlyList<LocationRow>> LockAsync(
        IReadOnlyCollection<long> locationIds, IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var warehouses = Where(l => locationIds.Contains(l.Id)).Select(l => l.WarehouseId).Concat(warehouseIds).ToHashSet();
        return Task.FromResult(Where(l => warehouses.Contains(l.WarehouseId)));
    }

    public Task<IReadOnlyList<LocationOptionDto>> FindOptionsAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LocationOptionDto>>(
            [.. Where(l => l.WarehouseId == warehouseId && l.UseYn).Select(l => new LocationOptionDto(l.Id, l.LocationCode, l.LocationName))]);

    public Task<IReadOnlyDictionary<long, string>> FindInactivationBlockersAsync(
        IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<long, string>>(
            Where(l => locationIds.Contains(l.Id) && l.OnhandQty != 0).ToDictionary(l => l.Id, _ => LocationBlockReasons.OnHand));

    public Task<IReadOnlyDictionary<long, string>> FindDeleteBlockersAsync(
        IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        FindInactivationBlockersAsync(locationIds, transaction, cancellationToken);

    public Task InsertAsync(LocationState location, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _rows.Add(new(_nextId++, location.WarehouseId, location.LocationCode, location.LocationName, false, location.UseYn, location.Remarks, 0, Stored));
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(LocationState location, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var index = _rows.FindIndex(l => l.Id == location.Id);
            _rows[index] = _rows[index] with
            {
                LocationName = location.LocationName,
                UseYn = location.UseYn,
                Remarks = location.Remarks,
                UpdatedAt = _rows[index].UpdatedAt.AddMinutes(1),
            };
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _rows.RemoveAll(l => locationIds.Contains(l.Id));
        }
        return Task.CompletedTask;
    }

    private IReadOnlyList<LocationRow> Where(Func<LocationRow, bool> predicate)
    {
        lock (_gate)
        {
            return [.. _rows.Where(predicate).OrderByDescending(l => l.IsSystem).ThenBy(l => l.LocationCode, StringComparer.Ordinal)];
        }
    }
}

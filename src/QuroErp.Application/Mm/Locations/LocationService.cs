using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Locations;

namespace QuroErp.Application.Mm.Locations;

/// <summary>Location (shelf) master per warehouse (MM-UC-034) and the Location choice list for select fields.</summary>
public sealed class LocationService(ILocationRepository locations, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>No warehouse, or one that does not exist. Arg: the field.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>The Locations of one warehouse, '*' first and then in code order.</summary>
    /// <exception cref="DomainException">422 Common-E-005 without a warehouse.</exception>
    public async Task<IReadOnlyList<LocationDto>> SearchAsync(LocationSearchQuery query, CancellationToken cancellationToken)
    {
        var warehouseId = query.WarehouseId ?? throw new DomainException(RequiredCode, "warehouseId");
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await locations.FindAsync(warehouseId, query.Code?.Trim(), query.IncludeInactive, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(ToDto)];
    }

    /// <summary>Locations in use of the warehouse, '*' first, for new document lines (MM-BR-717).</summary>
    public async Task<IReadOnlyList<LocationOptionDto>> GetOptionsAsync(long warehouseId, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var options = await locations.FindOptionsAsync(warehouseId, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return options;
    }

    /// <summary>
    /// Saves added, changed and deleted rows in one transaction and returns the Locations of the warehouses involved after the
    /// save. One failing row saves nothing.
    /// </summary>
    /// <exception cref="DomainException">422 MM-E-712 (code), MM-E-717 (code, reason), Common-E-005 (no or unknown warehouse).</exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when a row changed after it was read.</exception>
    public async Task<IReadOnlyList<LocationDto>> SaveAsync(
        BatchRequest<LocationSaveRow, LocationKey> request, string updatedBy, CancellationToken cancellationToken)
    {
        IReadOnlyList<LocationSaveRow> added = request.Added ?? [];
        IReadOnlyList<LocationSaveRow> updated = request.Updated ?? [];
        IReadOnlyList<LocationKey> deleted = request.Deleted ?? [];

        // New rows name their warehouse; changed and deleted rows find it from the stored row
        var addedWarehouses = added
            .Select(a => a.WarehouseId ?? throw new DomainException(RequiredCode, "warehouseId"))
            .ToHashSet();
        var namedIds = updated.Select(u => u.Id ?? 0).Concat(deleted.Select(d => d.Id)).ToHashSet();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        foreach (var warehouseId in addedWarehouses)
        {
            if (!await locations.WarehouseExistsAsync(warehouseId, uow.Transaction, cancellationToken))
            {
                throw new DomainException(RequiredCode, "warehouseId");
            }
        }
        var stored = (await locations.LockAsync(namedIds, addedWarehouses, uow.Transaction, cancellationToken)).ToDictionary(l => l.Id);

        // Rows the save names must still be as they were read; otherwise someone else saved first
        foreach (var (id, updatedAt) in updated.Select(u => (u.Id ?? 0, u.UpdatedAt)).Concat(deleted.Select(d => (d.Id, (DateTimeOffset?)d.UpdatedAt))))
        {
            if (!stored.TryGetValue(id, out var row) || row.UpdatedAt != updatedAt)
            {
                throw new ConcurrencyException();
            }
        }

        var deletedIds = deleted.Select(d => d.Id).ToHashSet();
        foreach (var id in deletedIds)
        {
            LocationRules.EnsureDeletable(stored[id].ToState());
        }
        var changedRows = updated.Select(row =>
        {
            var current = stored[row.Id!.Value].ToState();
            LocationRules.EnsureChangeable(current, row.LocationCode);
            return LocationRules.Normalize(current with { LocationName = row.LocationName, UseYn = row.UseYn ?? true, Remarks = row.Remarks });
        }).ToList();
        var newRows = added.Select(row => LocationRules.Normalize(new LocationState(
            0, row.WarehouseId!.Value, LocationRules.NormalizeCode(row.LocationCode), row.LocationName, false, row.UseYn ?? true, row.Remarks)))
            .ToList();

        var changedIds = changedRows.Select(r => r.Id).ToHashSet();
        var kept = stored.Values.Where(l => !deletedIds.Contains(l.Id) && !changedIds.Contains(l.Id)).Select(l => l.ToState());
        LocationRules.EnsureUnique([.. kept, .. changedRows, .. newRows]);

        // MM-BR-717: a balance blocks setting not in use; a balance or movements block a delete
        var inactivated = changedRows.Where(r => !r.UseYn && stored[r.Id].UseYn).Select(r => r.Id).ToList();
        EnsureNotBlocked(await locations.FindInactivationBlockersAsync(inactivated, uow.Transaction, cancellationToken), stored);
        EnsureNotBlocked(await locations.FindDeleteBlockersAsync([.. deletedIds], uow.Transaction, cancellationToken), stored);

        // Every row is locked and was checked against the values read. Deletes first: a new row may reuse a deleted code
        await locations.DeleteAsync(deletedIds, uow.Transaction, cancellationToken);
        foreach (var row in changedRows)
        {
            await locations.UpdateAsync(row, updatedBy, uow.Transaction, cancellationToken);
        }
        foreach (var row in newRows)
        {
            await locations.InsertAsync(row, updatedBy, uow.Transaction, cancellationToken);
        }

        var warehouseIds = stored.Values.Select(l => l.WarehouseId).Concat(addedWarehouses).Distinct().Order().ToList();
        var after = new List<LocationDto>();
        foreach (var warehouseId in warehouseIds)
        {
            after.AddRange((await locations.FindAsync(warehouseId, null, true, uow.Transaction, cancellationToken)).Select(ToDto));
        }
        await uow.CommitAsync(cancellationToken);
        return after;
    }

    // MM-E-717 names the first blocked Location in code order and why
    private static void EnsureNotBlocked(IReadOnlyDictionary<long, string> blockers, Dictionary<long, LocationRow> stored)
    {
        if (blockers.Count > 0)
        {
            var (id, reason) = blockers.OrderBy(b => stored[b.Key].LocationCode, StringComparer.Ordinal).First();
            throw new DomainException(LocationRules.InUseCode, stored[id].LocationCode, reason);
        }
    }

    private static LocationDto ToDto(LocationRow r) =>
        new(r.Id, r.WarehouseId, r.LocationCode, r.LocationName, r.IsSystem, r.UseYn, r.Remarks, r.OnhandQty, r.UpdatedAt);
}

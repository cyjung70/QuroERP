using System.Data;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Warehouses;

namespace QuroErp.Application.Mm.Warehouses;

/// <summary>Warehouse master (MM-UC-017) and the warehouse choice list for select fields (TASK-CORE-022).</summary>
public sealed class WarehouseService(IWarehouseRepository warehouses, IBusinessClock clock, IUnitOfWorkFactory unitOfWork)
{
    /// <param name="includeConsignment">true for report conditions only; document and default warehouse fields leave it out (MM-BR-710).</param>
    /// <param name="includeInactive">true adds warehouses not in use, for report and search filters.</param>
    public async Task<IReadOnlyList<WarehouseOptionDto>> GetOptionsAsync(
        bool includeConsignment, bool includeInactive, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var options = await warehouses.FindOptionsAsync(includeConsignment, includeInactive, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return options;
    }

    /// <summary>Every warehouse in code order; warehouses not in use only with includeInactive.</summary>
    public async Task<IReadOnlyList<WarehouseDto>> SearchAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await warehouses.FindAllAsync(forUpdate: false, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Where(r => includeInactive || r.UseYn).Select(ToDto)];
    }

    /// <summary>
    /// Saves added, changed and deleted rows in one transaction and returns every warehouse after the save. One failing row saves
    /// nothing. A row made the default switches the previous default off (MM-BR-702).
    /// </summary>
    /// <exception cref="DomainException">422 MM-E-701, MM-E-702, MM-E-704 (code, reason) or MM-E-709.</exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when a row changed after it was read.</exception>
    public async Task<IReadOnlyList<WarehouseDto>> SaveAsync(
        BatchRequest<WarehouseSaveRow, WarehouseKey> request, string updatedBy, CancellationToken cancellationToken)
    {
        IReadOnlyList<WarehouseSaveRow> added = request.Added ?? [];
        IReadOnlyList<WarehouseSaveRow> updated = request.Updated ?? [];
        IReadOnlyList<WarehouseKey> deleted = request.Deleted ?? [];

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var stored = (await warehouses.FindAllAsync(forUpdate: true, uow.Transaction, cancellationToken)).ToDictionary(w => w.Id);

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
            WarehouseRules.EnsureDeletable(stored[id].ToState());
        }
        var changedRows = updated.Select(row =>
        {
            var current = stored[row.Id!.Value].ToState();
            WarehouseRules.EnsureKeysUnchanged(current, row.WarehouseCode, row.WarehouseType);
            return WarehouseRules.Normalize(current with
            {
                WarehouseName = row.WarehouseName ?? "",
                IsDefault = row.IsDefault ?? false,
                UseYn = row.UseYn ?? true,
                Remarks = row.Remarks,
            });
        }).ToList();
        var newRows = added.Select(row =>
        {
            var code = WarehouseRules.NormalizeCode(row.WarehouseCode);
            return WarehouseRules.Normalize(new WarehouseState(
                0, code, row.WarehouseName ?? "", row.IsDefault ?? false, row.UseYn ?? true, WarehouseRules.NewWarehouseType(code, row.WarehouseType),
                row.Remarks));
        }).ToList();

        // MM-BR-702: a row made the default switches the stored default off
        var changedIds = changedRows.Select(r => r.Id).ToHashSet();
        var defaultMoves = changedRows.Concat(newRows).Any(r => r.IsDefault && !(r.Id != 0 && stored[r.Id].IsDefault));
        var kept = stored.Values.Where(w => !deletedIds.Contains(w.Id) && !changedIds.Contains(w.Id))
            .Select(w => w.ToState())
            .Select(w => defaultMoves ? w with { IsDefault = false } : w)
            .ToList();
        WarehouseRules.EnsureFinalState([.. kept, .. changedRows, .. newRows]);

        // MM-BR-704: stock and movements decide whether a warehouse can be set not in use or deleted
        var inactivated = changedRows.Where(r => !r.UseYn && stored[r.Id].UseYn).Select(r => r.Id).ToList();
        EnsureNotBlocked(await warehouses.FindInactivationBlockersAsync(inactivated, clock.Today(), uow.Transaction, cancellationToken), stored);
        EnsureNotBlocked(await warehouses.FindDeleteBlockersAsync([.. deletedIds], uow.Transaction, cancellationToken), stored);

        // Every row is locked and was checked against the values read, so the writes need no second updatedAt check.
        // Deletes first: a new row may reuse a deleted code. The old default goes off before another row becomes the default
        await warehouses.DeleteAsync(deletedIds, uow.Transaction, cancellationToken);
        if (defaultMoves)
        {
            await warehouses.ClearDefaultAsync(updatedBy, uow.Transaction, cancellationToken);
        }
        foreach (var row in changedRows.OrderBy(r => r.IsDefault))
        {
            await warehouses.UpdateAsync(row, updatedBy, uow.Transaction, cancellationToken);
        }
        foreach (var row in newRows)
        {
            await warehouses.InsertAsync(row, updatedBy, uow.Transaction, cancellationToken);
        }

        var after = await warehouses.FindAllAsync(forUpdate: false, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. after.Select(ToDto)];
    }

    // MM-E-704 names the first blocked warehouse in code order and why
    private static void EnsureNotBlocked(IReadOnlyDictionary<long, string> blockers, Dictionary<long, WarehouseRow> stored)
    {
        if (blockers.Count > 0)
        {
            var (id, reason) = blockers.OrderBy(b => stored[b.Key].WarehouseCode, StringComparer.Ordinal).First();
            throw new DomainException(WarehouseRules.InUseCode, stored[id].WarehouseCode, reason);
        }
    }

    private static WarehouseDto ToDto(WarehouseRow r) =>
        new(r.Id, r.WarehouseCode, r.WarehouseName, r.IsDefault, r.UseYn, r.WarehouseType, r.Remarks, r.UpdatedAt);
}

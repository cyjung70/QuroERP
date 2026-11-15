using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Mm.Pallets;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Mm.Pallets;

/// <summary>
/// Pallet management (MM-UC-035): search, a Pallet with its history, the scan lookup, split and merge. Split and merge move no
/// stock: the ACTIVE Pallet qty of the stock key stays the same, so MM-BR-916 holds.
/// </summary>
public sealed class PalletService(
    IPalletRepository pallets, DocumentNumberService numbers, IBusinessClock clock, IUnitOfWorkFactory unitOfWork)
{
    /// <summary>No Pallet with this id or number. Arg: the id or number.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>Pallets that match every given condition, in number order, at most <see cref="PalletRoutes.MaxListSize"/> rows.</summary>
    public async Task<IReadOnlyList<PalletDto>> SearchAsync(PalletSearchQuery query, CancellationToken cancellationToken)
    {
        var filter = new PalletFilter(
            Upper(query.PalletNo), query.ItemId, Upper(query.LotNo), query.WarehouseId, query.LocationId, Upper(query.Status),
            PalletRoutes.MaxListSize);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var rows = await pallets.SearchAsync(filter, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return [.. rows.Select(r => ToDto(r))];
    }

    /// <summary>The Pallet with its history, oldest first (MM-BR-916).</summary>
    /// <exception cref="NotFoundException">404 Common-E-006.</exception>
    public async Task<PalletDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await pallets.FindAsync(id, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var events = await pallets.FindEventsAsync(id, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(row, events);
    }

    /// <summary>The Pallet of a scanned number, ignoring case and spaces around it.</summary>
    /// <exception cref="NotFoundException">404 Common-E-006.</exception>
    public async Task<PalletDto> GetByNoAsync(string palletNo, CancellationToken cancellationToken)
    {
        var number = Upper(palletNo) ?? throw new NotFoundException(NotFoundCode, palletNo);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await pallets.FindByNoAsync(number, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, number);
        await uow.CommitAsync(cancellationToken);
        return ToDto(row);
    }

    /// <summary>
    /// MM-BR-914: splits qty off the Pallet into a new Pallet numbered in today's month, on the same item, Lot, warehouse and
    /// Location, with a SPLIT event on each side.
    /// </summary>
    /// <exception cref="NotFoundException">404 Common-E-006.</exception>
    /// <exception cref="ConcurrencyException">409 Common-E-004 when the Pallet changed after it was read.</exception>
    /// <exception cref="DomainException">422 MM-E-914.</exception>
    public async Task<SplitPalletResultDto> SplitAsync(long id, SplitPalletRequest request, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var pallet = (await pallets.LockAsync([id], uow.Transaction, cancellationToken)).SingleOrDefault()
            ?? throw new NotFoundException(NotFoundCode, id);
        if (pallet.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        PalletRules.Split(pallet.ToState(), request.Qty);

        var today = clock.Today();
        var palletNo = await numbers.NextAsync(DocumentNumberFormat.Pallet, today, user, uow.Transaction, cancellationToken);
        var newId = await pallets.SplitAsync(id, palletNo, request.Qty, today, user, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return new SplitPalletResultDto(newId, palletNo);
    }

    /// <summary>
    /// MM-BR-915: empties the source Pallets into the target, with a MERGE event on each side, and returns the target with its history.
    /// The Pallets are locked and checked as they are now, so one changed by someone else is refused by the rule.
    /// </summary>
    /// <exception cref="NotFoundException">404 Common-E-006 when a Pallet does not exist.</exception>
    /// <exception cref="DomainException">422 MM-E-915.</exception>
    public async Task<PalletDto> MergeAsync(MergePalletsRequest request, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<long> sourceIds = request.SourcePalletIds ?? [];
        var ids = sourceIds.Append(request.TargetPalletId).ToHashSet();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var locked = (await pallets.LockAsync(ids, uow.Transaction, cancellationToken)).ToDictionary(p => p.Id);
        var missing = ids.FirstOrDefault(i => !locked.ContainsKey(i));
        if (missing != 0 || ids.Contains(0))
        {
            throw new NotFoundException(NotFoundCode, missing);
        }
        PalletRules.Merge(locked[request.TargetPalletId].ToState(), [.. sourceIds.Select(i => locked[i].ToState())]);

        await pallets.MergeAsync(request.TargetPalletId, sourceIds, clock.Today(), user, uow.Transaction, cancellationToken);
        var target = await pallets.FindAsync(request.TargetPalletId, uow.Transaction, cancellationToken);
        var events = await pallets.FindEventsAsync(request.TargetPalletId, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(target!, events);
    }

    private static string? Upper(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static PalletDto ToDto(PalletRow r, IReadOnlyList<PalletEventRow>? events = null) =>
        new(r.Id, r.PalletNo, r.ItemId, r.ItemCd, r.ItemName, r.LotId, r.LotNo, r.WarehouseId, r.WarehouseCode, r.LocationId, r.LocationCode,
            r.Qty, r.Status, r.ParentPalletNo, r.UpdatedAt,
            events is null ? null : [.. events.Select(e => new PalletEventDto(e.EventType, e.EventDt, e.Qty, e.RelatedPalletNo, e.RefKey))]);
}

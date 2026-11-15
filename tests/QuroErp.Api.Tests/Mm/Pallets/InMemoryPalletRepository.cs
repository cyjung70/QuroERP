using System.Data;
using QuroErp.Application.Mm.Pallets;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Api.Tests.Mm.Pallets;

/// <summary>
/// mm_pallet in memory. Item BOLT (10) on warehouse MAIN (1), Location A-01 (2): PL2611000001 (6, lot L1), PL2611000002 (4, lot L1),
/// PL2611000003 (3, lot L2), PL2611000004 (CLOSED, lot L1) and PL2611000005 (5, lot L1). Each has a CREATE event of its first qty.
/// </summary>
public sealed class InMemoryPalletRepository : IPalletRepository
{
    public static readonly DateTimeOffset Stored = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly List<PalletRow> _rows =
    [
        Row(1, 6m, 20, "L1"),
        Row(2, 4m, 20, "L1"),
        Row(3, 3m, 21, "L2"),
        Row(4, 0m, 20, "L1") with { Status = PalletRules.Closed },
        Row(5, 5m, 20, "L1"),
    ];

    private readonly List<(long PalletId, PalletEventRow Event)> _events =
    [
        .. new[] { (1L, 6m), (2L, 4m), (3L, 3m), (4L, 2m), (5L, 5m) }
            .Select(p => (p.Item1, new PalletEventRow("CREATE", new DateOnly(2026, 11, 1), p.Item2, null, "GR2611000001"))),
    ];

    private long _nextId = 6;

    public Task<IReadOnlyList<PalletRow>> SearchAsync(PalletFilter filter, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Where(p =>
            (filter.PalletNo is null || p.PalletNo.StartsWith(filter.PalletNo, StringComparison.Ordinal))
            && (filter.ItemId is null || p.ItemId == filter.ItemId)
            && (filter.LotNo is null || p.LotNo == filter.LotNo)
            && (filter.WarehouseId is null || p.WarehouseId == filter.WarehouseId)
            && (filter.LocationId is null || p.LocationId == filter.LocationId)
            && (filter.Status is null || p.Status == filter.Status)));

    public Task<PalletRow?> FindAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Where(p => p.Id == id).SingleOrDefault());

    public Task<PalletRow?> FindByNoAsync(string palletNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Where(p => p.PalletNo == palletNo).SingleOrDefault());

    public Task<IReadOnlyList<PalletEventRow>> FindEventsAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<PalletEventRow>>([.. _events.Where(e => e.PalletId == id).Select(e => e.Event)]);
        }
    }

    public Task<IReadOnlyList<PalletRow>> LockAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Where(p => ids.Contains(p.Id)));

    public Task<long> SplitAsync(
        long sourceId, string palletNo, decimal qty, DateOnly eventDt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var index = _rows.FindIndex(p => p.Id == sourceId);
            var source = _rows[index];
            var id = _nextId++;
            _rows[index] = source with { Qty = source.Qty - qty, UpdatedAt = source.UpdatedAt.AddSeconds(1) };
            _rows.Add(source with { Id = id, PalletNo = palletNo, Qty = qty, ParentPalletNo = source.PalletNo });
            _events.Add((sourceId, new PalletEventRow(PalletRules.SplitEvent, eventDt, -qty, palletNo, null)));
            _events.Add((id, new PalletEventRow(PalletRules.SplitEvent, eventDt, qty, source.PalletNo, null)));
            return Task.FromResult(id);
        }
    }

    public Task MergeAsync(
        long targetId, IReadOnlyCollection<long> sourceIds, DateOnly eventDt, string user, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var target = _rows.FindIndex(p => p.Id == targetId);
            foreach (var sourceId in sourceIds)
            {
                var index = _rows.FindIndex(p => p.Id == sourceId);
                var source = _rows[index];
                _rows[index] = source with { Qty = 0m, Status = PalletRules.Closed };
                _rows[target] = _rows[target] with { Qty = _rows[target].Qty + source.Qty };
                _events.Add((sourceId, new PalletEventRow(PalletRules.MergeEvent, eventDt, -source.Qty, _rows[target].PalletNo, null)));
                _events.Add((targetId, new PalletEventRow(PalletRules.MergeEvent, eventDt, source.Qty, source.PalletNo, null)));
            }
            return Task.CompletedTask;
        }
    }

    public Task InsertAsync(IReadOnlyList<NewPallet> pallets, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ReceiptPalletRow>> LockByReceiptLinesAsync(
        IReadOnlyCollection<long> goodsReceiptLineIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task CloseForDeletedLinesAsync(
        IReadOnlyCollection<long> palletIds, DateOnly eventDt, string refKey, string user, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> FindNumbersByReceiptAsync(
        long goodsReceiptId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PalletLabelRow>> FindReceiptLabelsAsync(
        long goodsReceiptId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    private IReadOnlyList<PalletRow> Where(Func<PalletRow, bool> match)
    {
        lock (_gate)
        {
            return [.. _rows.Where(match).OrderBy(p => p.PalletNo, StringComparer.Ordinal)];
        }
    }

    private static PalletRow Row(long id, decimal qty, long lotId, string lotNo) =>
        new(id, $"PL261100000{id}", 10, "BOLT", "Hex Bolt", lotId, lotNo, 1, "MAIN", 2, "A-01", qty, PalletRules.Active, null, Stored);
}

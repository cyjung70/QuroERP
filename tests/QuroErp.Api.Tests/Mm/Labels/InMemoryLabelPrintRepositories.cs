using System.Collections.Concurrent;
using System.Data;
using QuroErp.Application.Mm.Labels;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Api.Tests.Mm.Labels;

/// <summary>
/// Print targets in memory: Pallets PL2611000001 (6) and PL2611000002 (4) of BOLT lot L1 on MAIN A-01 from receipt 5, item BOLT
/// (10), Lot L1 (20) and Location A-01 of MAIN (30, warehouse 1).
/// </summary>
public sealed class InMemoryLabelTargetRepository : ILabelTargetRepository
{
    private static readonly DateOnly Expiry = new(2027, 3, 31);

    private static readonly (LabelTargetDto Target, long? GoodsReceiptId, long? ItemId, long? WarehouseId)[] All =
    [
        (new(1, LabelType.Pallet, "PL2611000001", "Hex Bolt", "BOLT", "L1", 6, Expiry, "MAIN", "A-01"), 5, 10, 1),
        (new(2, LabelType.Pallet, "PL2611000002", "Hex Bolt", "BOLT", "L1", 4, Expiry, "MAIN", "A-01"), 5, 10, 1),
        (new(10, LabelType.Item, "BOLT", "Hex Bolt", "BOLT", null, null, null, null, null), 5, 10, null),
        (new(20, LabelType.Lot, "L1", "Hex Bolt", "BOLT", "L1", null, Expiry, null, null), 5, 10, null),
        (new(30, LabelType.Location, "A-01", "Shelf A1", null, null, null, null, "MAIN", "A-01"), null, null, 1),
    ];

    public Task<IReadOnlyList<LabelTargetDto>> FindAsync(
        string labelType, LabelTargetFilter filter, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LabelTargetDto>>([.. All
            .Where(t => t.Target.LabelType == labelType
                && (filter.Ids is null || filter.Ids.Contains(t.Target.Id))
                && (filter.GoodsReceiptId is null || t.GoodsReceiptId == filter.GoodsReceiptId)
                && (filter.ItemId is null || t.ItemId == filter.ItemId)
                && (filter.WarehouseId is null || t.WarehouseId == filter.WarehouseId))
            .Select(t => t.Target)
            .Take(filter.Limit)]);
}

/// <summary>mm_label_print in memory: what the prints wrote, read back newest first.</summary>
public sealed class InMemoryLabelPrintRepository : ILabelPrintRepository, ILabelPrintHistoryRepository
{
    private readonly ConcurrentQueue<LabelPrintHistoryDto> _rows = new();
    private long _nextId;

    public IReadOnlyList<LabelPrintHistoryDto> Rows => [.. _rows];

    public Task AddAsync(IReadOnlyList<LabelPrintRow> rows, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            _rows.Enqueue(new LabelPrintHistoryDto(
                Interlocked.Increment(ref _nextId), row.LabelType, row.RefId, null, row.Copies, row.OutputKind, row.PrinterName,
                DateTimeOffset.UtcNow, row.PrintedBy));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LabelPrintHistoryDto>> FindAsync(
        DateTimeOffset? printedFrom, DateTimeOffset? printedBefore, string? labelType, int limit, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LabelPrintHistoryDto>>([.. _rows
            .Where(r => (printedFrom is null || r.PrintedAt >= printedFrom) && (printedBefore is null || r.PrintedAt < printedBefore)
                && (labelType is null || r.LabelType == labelType))
            .OrderByDescending(r => r.Id)
            .Take(limit)]);
}

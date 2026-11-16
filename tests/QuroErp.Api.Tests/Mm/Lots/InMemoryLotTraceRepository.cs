using System.Data;
using QuroErp.Application.Mm.Lots;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Api.Tests.Mm.Lots;

/// <summary>
/// One received Lot for the Lot trace API tests: item 501, Lot 261104-001 from receipt GR-001 of Vendor A, 3 of it shipped to
/// Customer A on SH-001. The joins and the netting are covered by LotTraceServiceTests on real PostgreSQL.
/// </summary>
public sealed class InMemoryLotTraceRepository : ILotTraceRepository
{
    public const long ItemId = 501;
    public const string LotNo = "261104-001";
    public const string ShipNo = "SH-001";

    private static readonly DateOnly Day = new(2026, 11, 4);

    private static readonly LotTraceLot Lot = new(1, ItemId, "CABLE", "Cable", LotNo, Day, "GR-001", 1, Day, "Vendor A", 10m, null, null, null);

    private static readonly LotTraceMovement[] Movements =
    [
        new(1, "GR", "GR-001", 1, Day, 1, "MAIN", ItemId, "CABLE", "Cable", LotNo, 10m, "GR", "Vendor A"),
        new(1, "DN", ShipNo, 1, Day.AddDays(1), 1, "MAIN", ItemId, "CABLE", "Cable", LotNo, -3m, "SHIP", "Customer A"),
    ];

    public Task<LotTraceLot?> FindLotAsync(long itemId, string lotNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(itemId == ItemId && lotNo == LotNo ? Lot : null);

    public Task<IReadOnlyList<LotTraceLot>> FindLotsAsync(IReadOnlyCollection<long> lotIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LotTraceLot>>(lotIds.Contains(Lot.Id) ? [Lot] : []);

    public Task<IReadOnlyList<LotTraceMovement>> FindMovementsAsync(
        IReadOnlyCollection<long> lotIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LotTraceMovement>>([.. Movements.Where(m => lotIds.Contains(m.LotId))]);

    public Task<IReadOnlyList<LotTraceLink>> FindLinksAsync(IReadOnlyCollection<long> lotIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LotTraceLink>>([]);

    public Task<IReadOnlyList<LotTraceMovement>> FindIssuesAsync(
        LotTraceIssueSource source, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LotTraceMovement>>(source.DocNo == ShipNo ? [Movements[1]] : []);
}

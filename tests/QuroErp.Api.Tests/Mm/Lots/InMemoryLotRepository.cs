using System.Data;
using QuroErp.Application.Mm.Lots;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Api.Tests.Mm.Lots;

/// <summary>
/// Stock balances per item + warehouse for the stock suggestion API tests. Item 501 in warehouse 1 has 6: 2 on Lot '*' and
/// 4 on Lot 260901-001 of Location A01. The date is not used.
/// </summary>
public sealed class InMemoryLotRepository : ILotRepository
{
    public const long ItemId = 501;
    public const long WarehouseId = 1;

    private readonly Dictionary<(long ItemId, long WarehouseId), List<StockBalance>> _balances = new()
    {
        [(ItemId, WarehouseId)] =
        [
            new(10, "A01", 21, "260901-001", new DateOnly(2026, 9, 1), 4),
            new(1, "*", 20, "*", null, 2),
        ],
    };

    public Task<IReadOnlyList<StockBalance>> FindBalancesAsync(
        long itemId, long warehouseId, DateOnly asOf, bool withoutPallets, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StockBalance>>(_balances.TryGetValue((itemId, warehouseId), out var rows) ? [.. rows] : []);

    public Task<bool> HasLotStockAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> HasLocationStockAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<OnHandMismatchRow>> FindOnHandMismatchesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OnHandMismatchRow>>([]);
}

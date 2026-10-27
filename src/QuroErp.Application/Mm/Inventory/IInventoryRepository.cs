using System.Data;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Mm.Inventory;

/// <summary>Locked mm_onhand row of one item + warehouse, with the codes used in messages.</summary>
public sealed record OnHandRow(long ItemId, long WarehouseId, string WarehouseCode, decimal OnHandQty);

/// <summary>A saved movement and whether a reversal already points to it.</summary>
public sealed record SavedMovement(GoodsMovement Movement, bool Reversed);

/// <summary>Stock tables: bc_item stock columns, mm_onhand and mm_goods_movement (inventory.md).</summary>
public interface IInventoryRepository
{
    /// <summary>bc_company.close_year_month: the first month not closed yet (YYYYMM), or null.</summary>
    Task<string?> GetCloseYearMonthAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Reads and locks (FOR UPDATE, in id order) the stock columns of the items.</summary>
    /// <returns>Only the items that exist.</returns>
    Task<IReadOnlyList<ItemStock>> LockItemsAsync(IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the mm_onhand row of item + warehouse (FOR UPDATE), creating it with 0 when missing (MM-BR-603 ①).
    /// Saves of the same item + warehouse wait here for each other.
    /// </summary>
    Task<OnHandRow> LockOnHandAsync(long itemId, long warehouseId, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    Task UpdateOnHandAsync(long itemId, long warehouseId, decimal onHandQty, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves bc_item.onhand_qty, stk_val and mvg_cost.</summary>
    Task UpdateItemStockAsync(ItemStock item, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Inserts the movement and sets its Id.</summary>
    Task InsertMovementAsync(GoodsMovement movement, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Ledger balances of item + warehouse for the point-in-time checks (MM-BR-604), read in one query before the
    /// new movement is inserted. Opening is the closing balance of lastClosedMonthEnd's month (0 when null).
    /// Balances start at the last day with movements on or before movementDate and include every later day.
    /// </summary>
    Task<(decimal Opening, IReadOnlyList<DailyStockBalance> Balances)> GetDailyBalancesAsync(
        long itemId, long warehouseId, DateOnly? lastClosedMonthEnd, DateOnly movementDate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Reads and locks (FOR UPDATE) movements to reverse.</summary>
    /// <returns>Only the movements that exist.</returns>
    Task<IReadOnlyList<SavedMovement>> LockMovementsAsync(IReadOnlyCollection<long> movementIds, IDbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>
/// Set item components (MM-BR-007). Source table: sd_bom (AS IS P_BOM), not generated yet [확인 필요].
/// </summary>
public interface ISetComponentRepository
{
    Task<IReadOnlyList<SetComponent>> GetComponentsAsync(IReadOnlyCollection<long> setItemIds, IDbTransaction transaction, CancellationToken cancellationToken);
}

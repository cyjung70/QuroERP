using System.Data;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Application.Mm.Inventory;

/// <summary>Locked mm_onhand row of one item + warehouse, with the codes used in messages.</summary>
public sealed record OnHandRow(long ItemId, long WarehouseId, string WarehouseCode, decimal OnHandQty);

/// <summary>An mm_lot row: one lot number of one item (MM-BR-352).</summary>
public sealed record LotRef(long Id, string LotNo);

/// <summary>A saved movement and whether a reversal already points to it.</summary>
public sealed record SavedMovement(GoodsMovement Movement, bool Reversed);

/// <summary>Stock tables: bc_item stock columns, mm_onhand, mm_lot, mm_lot_onhand and mm_goods_movement (inventory.md).</summary>
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

    /// <summary>
    /// Sellable stock per item: the on-hand quantity of every warehouse except the consignment warehouse (MM-BR-711). For
    /// available quantities on sales orders, the cart and shipments, and the current stock of the purchase simulation.
    /// Stock reports, stock value, month close and reconciliation keep the consignment stock.
    /// </summary>
    /// <returns>Every requested item; 0 for an item without on-hand rows.</returns>
    Task<IReadOnlyDictionary<long, decimal>> GetSellableOnHandAsync(
        IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken);

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

    /// <summary>A lot of the item by id when lotId is given, otherwise by lot number; null when the item has no such lot.</summary>
    Task<LotRef?> FindLotAsync(long itemId, long? lotId, string? lotNo, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The lot of the item with this number, created when missing (MM-BR-353). The item row is locked by the caller.</summary>
    Task<LotRef> EnsureLotAsync(long itemId, string lotNo, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The largest number after prefix (yymmdd-) among the item's automatic lot numbers, 0 when none (MM-BR-353).</summary>
    Task<int> GetLastAutoLotSeqAsync(long itemId, string prefix, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the mm_lot_onhand row of item + warehouse + lot (FOR UPDATE), creating it with 0 when missing. It is locked after
    /// the mm_onhand rows (inventory.md "로트").
    /// </summary>
    /// <returns>The lot balance.</returns>
    Task<decimal> LockLotOnHandAsync(long itemId, long warehouseId, long lotId, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    Task UpdateLotOnHandAsync(
        long itemId, long warehouseId, long lotId, decimal onHandQty, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Ledger sums of item + warehouse + lot per day, from fromDate on, in date order (MM-BR-356).</summary>
    Task<IReadOnlyList<LotDailyQty>> GetLotDailyQtyAsync(
        long itemId, long warehouseId, long lotId, DateOnly fromDate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Reads and locks (FOR UPDATE) movements to reverse.</summary>
    /// <returns>Only the movements that exist.</returns>
    Task<IReadOnlyList<SavedMovement>> LockMovementsAsync(IReadOnlyCollection<long> movementIds, IDbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>Set item components (MM-BR-007) from sd_bom (AS IS P_BOM).</summary>
public interface ISetComponentRepository
{
    /// <summary>
    /// The components of the given items, without locking. Items that are not sets have none. The inventory service
    /// reads them before and again after locking the item rows (inventory.md step 0).
    /// </summary>
    Task<IReadOnlyList<SetComponent>> GetComponentsAsync(IReadOnlyCollection<long> setItemIds, IDbTransaction transaction, CancellationToken cancellationToken);
}

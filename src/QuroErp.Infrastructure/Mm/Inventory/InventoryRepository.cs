using System.Data;
using System.Globalization;
using Dapper;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Infrastructure.Mm.Inventory;

internal sealed class InventoryRepository : IInventoryRepository
{
    // MVP has one company row (bc_company)
    private const string SelectCloseYearMonth = """
        SELECT close_year_month FROM bc_company ORDER BY id LIMIT 1
        """;

    private const string LockItems = """
        SELECT id AS item_id, item_cd, type,
               coalesce(onhand_qty, 0) AS onhand_qty, coalesce(stk_val, 0) AS stk_val, coalesce(mvg_cost, 0) AS mvg_cost,
               lot_managed, lot_managed_from
        FROM bc_item
        WHERE id = ANY(@ItemIds)
        ORDER BY id
        FOR UPDATE
        """;

    private const string EnsureOnHand = """
        INSERT INTO mm_onhand (item_id, warehouse_id, onhand_qty, created_by, updated_by)
        VALUES (@ItemId, @WarehouseId, 0, @User, @User)
        ON CONFLICT (item_id, warehouse_id) DO NOTHING
        """;

    private const string LockOnHandRow = """
        SELECT o.item_id, o.warehouse_id, w.warehouse_code, o.onhand_qty
        FROM mm_onhand o
        JOIN bc_warehouse w ON w.id = o.warehouse_id
        WHERE o.item_id = @ItemId AND o.warehouse_id = @WarehouseId
        FOR UPDATE OF o
        """;

    private const string UpdateOnHand = """
        UPDATE mm_onhand
        SET onhand_qty = @OnHandQty, updated_at = now(), updated_by = @User
        WHERE item_id = @ItemId AND warehouse_id = @WarehouseId
        """;

    private const string UpdateItem = """
        UPDATE bc_item
        SET onhand_qty = @OnHandQty, stk_val = @StockValue, mvg_cost = @MovingAverageCost, updated_at = now(), updated_by = @User
        WHERE id = @ItemId
        """;

    private const string InsertMovement = """
        INSERT INTO mm_goods_movement (document_no, document_dt, mov_type, item_id, warehouse_id, qty, prc, amount,
                                       ref_key, ref_seq, parent_movement_id, negative_stock, set_item_id, lot_id, created_by, updated_by)
        VALUES (@DocumentNo, @DocumentDt, @MovType, @ItemId, @WarehouseId, @Qty, @Prc, @Amount,
                @RefKey, @RefSeq, @ParentMovementId, @NegativeStock, @SetItemId, @LotId, @User, @User)
        RETURNING id
        """;

    // MM-BR-604: closing balance of the last closed month + daily sums, one window pass. The LEFT JOIN keeps the
    // opening row when there are no movements. Rows start at the last day on or before @MovementDate
    private const string SelectDailyBalances = """
        WITH base AS (
          SELECT coalesce(sum(bal_qty), 0) AS qty FROM mm_monthly_inventory
           WHERE item_id = @ItemId AND warehouse_id = @WarehouseId AND year_month = @LastClosedYearMonth
        ), daily AS (
          SELECT document_dt, sum(qty) AS day_qty FROM mm_goods_movement
           WHERE item_id = @ItemId AND warehouse_id = @WarehouseId AND document_dt > @LastClosedMonthEnd
           GROUP BY document_dt
        ), running AS (
          SELECT document_dt,
                 (SELECT qty FROM base) + sum(day_qty) OVER (ORDER BY document_dt) AS balance,
                 row_number() OVER (PARTITION BY date_trunc('month', document_dt) ORDER BY document_dt DESC) AS rn_desc
            FROM daily
        )
        SELECT b.qty AS opening, r.document_dt, r.balance, r.rn_desc = 1 AS month_end
          FROM base b
          LEFT JOIN running r
            ON r.document_dt >= coalesce((SELECT max(document_dt) FROM daily WHERE document_dt <= @MovementDate), @MovementDate)
         ORDER BY r.document_dt
        """;

    private const string LockMovements = """
        SELECT m.id, m.document_no, m.document_dt, m.mov_type, m.item_id, m.warehouse_id, m.qty, m.prc,
               m.ref_key, m.ref_seq, m.parent_movement_id, m.negative_stock, m.set_item_id, m.lot_id,
               EXISTS (SELECT 1 FROM mm_goods_movement r WHERE r.parent_movement_id = m.id) AS reversed
        FROM mm_goods_movement m
        WHERE m.id = ANY(@MovementIds)
        ORDER BY m.id
        FOR UPDATE OF m
        """;

    private const string SelectLot = """
        SELECT id, lot_no FROM mm_lot
        WHERE item_id = @ItemId AND (CASE WHEN @LotId::bigint IS NULL THEN lot_no = @LotNo ELSE id = @LotId END)
        """;

    // The item row is locked by the caller, so two saves cannot create the same lot at once
    private const string EnsureLot = """
        INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
        VALUES (@ItemId, @LotNo, @User, @User)
        ON CONFLICT (item_id, lot_no) DO NOTHING
        """;

    // MM-BR-353: yymmdd-nnn, and more digits past 999
    private const string SelectLastAutoLotSeq = """
        SELECT coalesce(max(substr(lot_no, length(@Prefix) + 1)::int), 0)
        FROM mm_lot
        WHERE item_id = @ItemId AND starts_with(lot_no, @Prefix) AND substr(lot_no, length(@Prefix) + 1) ~ '^[0-9]{1,9}$'
        """;

    private const string EnsureLotOnHand = """
        INSERT INTO mm_lot_onhand (item_id, warehouse_id, lot_id, onhand_qty, created_by, updated_by)
        VALUES (@ItemId, @WarehouseId, @LotId, 0, @User, @User)
        ON CONFLICT (item_id, warehouse_id, lot_id) DO NOTHING
        """;

    private const string LockLotOnHandRow = """
        SELECT onhand_qty FROM mm_lot_onhand
        WHERE item_id = @ItemId AND warehouse_id = @WarehouseId AND lot_id = @LotId
        FOR UPDATE
        """;

    private const string UpdateLotOnHand = """
        UPDATE mm_lot_onhand
        SET onhand_qty = @OnHandQty, updated_at = now(), updated_by = @User
        WHERE item_id = @ItemId AND warehouse_id = @WarehouseId AND lot_id = @LotId
        """;

    private const string SelectLotDailyQty = """
        SELECT document_dt AS date, sum(qty) AS qty
        FROM mm_goods_movement
        WHERE item_id = @ItemId AND warehouse_id = @WarehouseId AND lot_id = @LotId AND document_dt >= @FromDate
        GROUP BY document_dt
        ORDER BY document_dt
        """;

    public async Task<string?> GetCloseYearMonthAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(SelectCloseYearMonth, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<ItemStock>> LockItemsAsync(IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await transaction.Connection!.QueryAsync<ItemRow>(
            new CommandDefinition(LockItems, new { ItemIds = itemIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        return rows.Select(r => new ItemStock(r.ItemId, r.ItemCd, r.Type, r.OnhandQty, r.StkVal, r.MvgCost, r.LotManaged, r.LotManagedFrom)).ToList();
    }

    public async Task<OnHandRow> LockOnHandAsync(long itemId, long warehouseId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var key = new { ItemId = itemId, WarehouseId = warehouseId, User = user };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(EnsureOnHand, key, transaction, cancellationToken: cancellationToken));
        var row = await transaction.Connection!.QuerySingleAsync<OnHandDbRow>(
            new CommandDefinition(LockOnHandRow, key, transaction, cancellationToken: cancellationToken));
        return new OnHandRow(row.ItemId, row.WarehouseId, row.WarehouseCode, row.OnhandQty);
    }

    public Task UpdateOnHandAsync(long itemId, long warehouseId, decimal onHandQty, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateOnHand,
            new { ItemId = itemId, WarehouseId = warehouseId, OnHandQty = onHandQty, User = user },
            transaction,
            cancellationToken: cancellationToken));

    public Task UpdateItemStockAsync(ItemStock item, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateItem,
            new { item.ItemId, item.OnHandQty, item.StockValue, item.MovingAverageCost, User = user },
            transaction,
            cancellationToken: cancellationToken));

    public async Task InsertMovementAsync(GoodsMovement movement, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        movement.Id = await transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            InsertMovement,
            new
            {
                movement.DocumentNo,
                movement.DocumentDt,
                movement.MovType,
                movement.ItemId,
                movement.WarehouseId,
                movement.Qty,
                movement.Prc,
                movement.Amount,
                movement.RefKey,
                movement.RefSeq,
                movement.ParentMovementId,
                movement.NegativeStock,
                movement.SetItemId,
                movement.LotId,
                User = user,
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<(decimal Opening, IReadOnlyList<DailyStockBalance> Balances)> GetDailyBalancesAsync(
        long itemId, long warehouseId, DateOnly? lastClosedMonthEnd, DateOnly movementDate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = (await transaction.Connection!.QueryAsync<BalanceRow>(new CommandDefinition(
            SelectDailyBalances,
            new
            {
                ItemId = itemId,
                WarehouseId = warehouseId,
                LastClosedYearMonth = lastClosedMonthEnd?.ToString("yyyyMM", CultureInfo.InvariantCulture),
                // No closed month: every movement counts
                LastClosedMonthEnd = lastClosedMonthEnd ?? DateOnly.MinValue,
                MovementDate = movementDate,
            },
            transaction,
            cancellationToken: cancellationToken))).ToList();
        var balances = rows.Where(r => r.DocumentDt is not null)
            .Select(r => new DailyStockBalance(r.DocumentDt!.Value, r.Balance!.Value, r.MonthEnd == true)).ToList();
        return (rows[0].Opening, balances);
    }

    public async Task<IReadOnlyList<SavedMovement>> LockMovementsAsync(
        IReadOnlyCollection<long> movementIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await transaction.Connection!.QueryAsync<MovementRow>(
            new CommandDefinition(LockMovements, new { MovementIds = movementIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        return rows.Select(r => new SavedMovement(
            GoodsMovement.Restore(r.Id, r.DocumentNo, r.MovType, r.DocumentDt, r.ItemId, r.WarehouseId, r.Qty, r.Prc,
                r.RefKey, r.RefSeq, r.ParentMovementId, r.NegativeStock, r.SetItemId, r.LotId),
            r.Reversed)).ToList();
    }

    public async Task<LotRef?> FindLotAsync(long itemId, long? lotId, string? lotNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.QuerySingleOrDefaultAsync<LotRef>(new CommandDefinition(
            SelectLot, new { ItemId = itemId, LotId = lotId, LotNo = lotNo }, transaction, cancellationToken: cancellationToken));

    public async Task<LotRef> EnsureLotAsync(long itemId, string lotNo, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            EnsureLot, new { ItemId = itemId, LotNo = lotNo, User = user }, transaction, cancellationToken: cancellationToken));
        return (await FindLotAsync(itemId, null, lotNo, transaction, cancellationToken))!;
    }

    public Task<int> GetLastAutoLotSeqAsync(long itemId, string prefix, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
            SelectLastAutoLotSeq, new { ItemId = itemId, Prefix = prefix }, transaction, cancellationToken: cancellationToken));

    public async Task<decimal> LockLotOnHandAsync(
        long itemId, long warehouseId, long lotId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var key = new { ItemId = itemId, WarehouseId = warehouseId, LotId = lotId, User = user };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(EnsureLotOnHand, key, transaction, cancellationToken: cancellationToken));
        return await transaction.Connection!.QuerySingleAsync<decimal>(
            new CommandDefinition(LockLotOnHandRow, key, transaction, cancellationToken: cancellationToken));
    }

    public Task UpdateLotOnHandAsync(
        long itemId, long warehouseId, long lotId, decimal onHandQty, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateLotOnHand,
            new { ItemId = itemId, WarehouseId = warehouseId, LotId = lotId, OnHandQty = onHandQty, User = user },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<LotDailyQty>> GetLotDailyQtyAsync(
        long itemId, long warehouseId, long lotId, DateOnly fromDate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<LotDailyQty>(new CommandDefinition(
            SelectLotDailyQty,
            new { ItemId = itemId, WarehouseId = warehouseId, LotId = lotId, FromDate = fromDate },
            transaction,
            cancellationToken: cancellationToken))).ToList();

    private sealed record ItemRow(
        long ItemId, string ItemCd, string? Type, decimal OnhandQty, decimal StkVal, decimal MvgCost, bool LotManaged, DateOnly? LotManagedFrom);

    private sealed record BalanceRow(decimal Opening, DateOnly? DocumentDt, decimal? Balance, bool? MonthEnd);

    private sealed record OnHandDbRow(long ItemId, long WarehouseId, string WarehouseCode, decimal OnhandQty);

    private sealed record MovementRow(
        long Id, string DocumentNo, DateOnly DocumentDt, string MovType, long ItemId, long WarehouseId, decimal Qty, decimal Prc,
        string? RefKey, int? RefSeq, long? ParentMovementId, bool NegativeStock, long? SetItemId, long? LotId, bool Reversed);
}

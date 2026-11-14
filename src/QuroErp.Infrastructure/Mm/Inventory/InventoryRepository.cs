using System.Data;
using System.Globalization;
using Dapper;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Domain.Mm.Inventory;

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
               lot_managed, lot_managed_from, location_managed
        FROM bc_item
        WHERE id = ANY(@ItemIds)
        ORDER BY id
        FOR UPDATE
        """;

    // Stock is kept per item + warehouse + Location + Lot (MM-BR-714). The ('*', '*') row of item + warehouse is where saves of
    // the same item + warehouse wait for each other (MM-BR-603 step 1); the item + warehouse balance is the sum of its rows.
    private const string EnsureOnHand = """
        INSERT INTO bc_location (warehouse_id, location_code, is_system, created_by, updated_by)
        VALUES (@WarehouseId, '*', true, @User, @User)
        ON CONFLICT (warehouse_id, location_code) DO NOTHING;
        INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
        VALUES (@ItemId, '*', @User, @User)
        ON CONFLICT (item_id, lot_no) DO NOTHING;
        INSERT INTO mm_onhand (item_id, warehouse_id, location_id, lot_id, onhand_qty, created_by, updated_by)
        SELECT @ItemId, @WarehouseId, c.id, l.id, 0, @User, @User
        FROM bc_location c, mm_lot l
        WHERE c.warehouse_id = @WarehouseId AND c.location_code = '*' AND l.item_id = @ItemId AND l.lot_no = '*'
        ON CONFLICT (item_id, warehouse_id, location_id, lot_id) DO NOTHING
        """;

    private const string StarKey = """
        location_id = (SELECT id FROM bc_location WHERE warehouse_id = @WarehouseId AND location_code = '*')
        AND lot_id = (SELECT id FROM mm_lot WHERE item_id = @ItemId AND lot_no = '*')
        """;

    private const string LockOnHandRow = $"""
        SELECT o.item_id, o.warehouse_id, w.warehouse_code
        FROM mm_onhand o
        JOIN bc_warehouse w ON w.id = o.warehouse_id
        WHERE o.item_id = @ItemId AND o.warehouse_id = @WarehouseId AND {StarKey}
        FOR UPDATE OF o
        """;

    // Read after the lock in its own statement, so it sees what a save that held the lock committed
    private const string SelectItemWarehouseOnHand = """
        SELECT coalesce(sum(onhand_qty), 0) FROM mm_onhand WHERE item_id = @ItemId AND warehouse_id = @WarehouseId
        """;

    private const string SelectSellableOnHand = """
        SELECT o.item_id, sum(o.onhand_qty) AS onhand_qty
        FROM mm_onhand o
        JOIN bc_warehouse w ON w.id = o.warehouse_id
        WHERE o.item_id = ANY(@ItemIds) AND w.warehouse_type <> 'CONSIGNMENT'
        GROUP BY o.item_id
        """;

    private const string SelectLocation = """
        SELECT id, warehouse_id, location_code, use_yn AS in_use FROM bc_location
        WHERE CASE WHEN @LocationId::bigint IS NULL THEN warehouse_id = @WarehouseId AND location_code = '*' ELSE id = @LocationId END
        """;

    // A key row of item + warehouse + Location + Lot; the ('*', '*') row of item + warehouse is locked before it
    private const string Key = """
        item_id = @ItemId AND warehouse_id = @WarehouseId AND location_id = @LocationId AND lot_id = @LotId
        """;

    private const string EnsureKeyOnHand = """
        INSERT INTO mm_onhand (item_id, warehouse_id, location_id, lot_id, onhand_qty, created_by, updated_by)
        VALUES (@ItemId, @WarehouseId, @LocationId, @LotId, 0, @User, @User)
        ON CONFLICT (item_id, warehouse_id, location_id, lot_id) DO NOTHING
        """;

    private const string LockKeyOnHandRow = $"""
        SELECT onhand_qty FROM mm_onhand WHERE {Key} FOR UPDATE
        """;

    private const string AddOnHand = $"""
        UPDATE mm_onhand SET onhand_qty = onhand_qty + @Qty, updated_at = now(), updated_by = @User WHERE {Key}
        """;

    private const string SelectKeyDailyQty = $"""
        SELECT document_dt AS date, sum(qty) AS qty
        FROM mm_goods_movement
        WHERE {Key} AND document_dt >= @FromDate
        GROUP BY document_dt
        ORDER BY document_dt
        """;

    private const string UpdateItem = """
        UPDATE bc_item
        SET onhand_qty = @OnHandQty, stk_val = @StockValue, mvg_cost = @MovingAverageCost, updated_at = now(), updated_by = @User
        WHERE id = @ItemId
        """;

    private const string InsertMovement = """
        INSERT INTO mm_goods_movement (document_no, document_dt, mov_type, item_id, warehouse_id, location_id, qty, prc, amount,
                                       ref_key, ref_seq, parent_movement_id, negative_stock, set_item_id, lot_id, created_by, updated_by)
        VALUES (@DocumentNo, @DocumentDt, @MovType, @ItemId, @WarehouseId,
                coalesce(@LocationId, (SELECT id FROM bc_location WHERE warehouse_id = @WarehouseId AND location_code = '*')),
                @Qty, @Prc, @Amount,
                @RefKey, @RefSeq, @ParentMovementId, @NegativeStock, @SetItemId,
                coalesce(@LotId, (SELECT id FROM mm_lot WHERE item_id = @ItemId AND lot_no = '*')), @User, @User)
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
               m.ref_key, m.ref_seq, m.parent_movement_id, m.negative_stock, m.set_item_id, m.lot_id, m.amount, m.location_id,
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

    // The item row is locked by the caller, so two saves cannot create the same lot at once. MM-BR-364: the received date is
    // the FIFO order of the lot (MM-BR-716)
    private const string EnsureLot = """
        INSERT INTO mm_lot (item_id, lot_no, received_dt, created_by, updated_by)
        VALUES (@ItemId, @LotNo, @ReceivedDt, @User, @User)
        ON CONFLICT (item_id, lot_no) DO NOTHING
        """;

    // MM-BR-353: yymmdd-nnn, and more digits past 999
    private const string SelectLastAutoLotSeq = """
        SELECT coalesce(max(substr(lot_no, length(@Prefix) + 1)::int), 0)
        FROM mm_lot
        WHERE item_id = @ItemId AND starts_with(lot_no, @Prefix) AND substr(lot_no, length(@Prefix) + 1) ~ '^[0-9]{1,9}$'
        """;

    public async Task<string?> GetCloseYearMonthAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(SelectCloseYearMonth, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<ItemStock>> LockItemsAsync(IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await transaction.Connection!.QueryAsync<ItemRow>(
            new CommandDefinition(LockItems, new { ItemIds = itemIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        return rows.Select(r => new ItemStock(
            r.ItemId, r.ItemCd, r.Type, r.OnhandQty, r.StkVal, r.MvgCost, r.LotManaged, r.LotManagedFrom, r.LocationManaged)).ToList();
    }

    public async Task<IReadOnlyDictionary<long, decimal>> GetSellableOnHandAsync(
        IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = (await transaction.Connection!.QueryAsync<(long ItemId, decimal OnHandQty)>(new CommandDefinition(
            SelectSellableOnHand, new { ItemIds = itemIds.ToArray() }, transaction, cancellationToken: cancellationToken)))
            .ToDictionary(r => r.ItemId, r => r.OnHandQty);
        return itemIds.Distinct().ToDictionary(id => id, id => rows.GetValueOrDefault(id));
    }

    public async Task<OnHandRow> LockOnHandAsync(long itemId, long warehouseId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var key = new { ItemId = itemId, WarehouseId = warehouseId, User = user };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(EnsureOnHand, key, transaction, cancellationToken: cancellationToken));
        var row = await transaction.Connection!.QuerySingleAsync<OnHandDbRow>(
            new CommandDefinition(LockOnHandRow, key, transaction, cancellationToken: cancellationToken));
        var onHandQty = await transaction.Connection!.ExecuteScalarAsync<decimal>(
            new CommandDefinition(SelectItemWarehouseOnHand, key, transaction, cancellationToken: cancellationToken));
        return new OnHandRow(row.ItemId, row.WarehouseId, row.WarehouseCode, onHandQty);
    }

    public async Task<StockLocation?> FindLocationAsync(
        long warehouseId, long? locationId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.QuerySingleOrDefaultAsync<StockLocation>(new CommandDefinition(
            SelectLocation, new { WarehouseId = warehouseId, LocationId = locationId }, transaction, cancellationToken: cancellationToken));

    public async Task<decimal> LockKeyOnHandAsync(
        long itemId, long warehouseId, long locationId, long lotId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var key = new { ItemId = itemId, WarehouseId = warehouseId, LocationId = locationId, LotId = lotId, User = user };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(EnsureKeyOnHand, key, transaction, cancellationToken: cancellationToken));
        return await transaction.Connection!.QuerySingleAsync<decimal>(
            new CommandDefinition(LockKeyOnHandRow, key, transaction, cancellationToken: cancellationToken));
    }

    public Task AddOnHandAsync(
        long itemId, long warehouseId, long locationId, long lotId, decimal qty, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            AddOnHand,
            new { ItemId = itemId, WarehouseId = warehouseId, LocationId = locationId, LotId = lotId, Qty = qty, User = user },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<StockKeyDailyQty>> GetKeyDailyQtyAsync(
        long itemId, long warehouseId, long locationId, long lotId, DateOnly fromDate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<StockKeyDailyQty>(new CommandDefinition(
            SelectKeyDailyQty,
            new { ItemId = itemId, WarehouseId = warehouseId, LocationId = locationId, LotId = lotId, FromDate = fromDate },
            transaction,
            cancellationToken: cancellationToken))).ToList();

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
                movement.LocationId,
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
                r.RefKey, r.RefSeq, r.ParentMovementId, r.NegativeStock, r.SetItemId, r.LotId, r.Amount, r.LocationId),
            r.Reversed)).ToList();
    }

    public async Task<LotRef?> FindLotAsync(long itemId, long? lotId, string? lotNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.QuerySingleOrDefaultAsync<LotRef>(new CommandDefinition(
            SelectLot, new { ItemId = itemId, LotId = lotId, LotNo = lotNo }, transaction, cancellationToken: cancellationToken));

    public async Task<LotRef> EnsureLotAsync(
        long itemId, string lotNo, DateOnly receivedDt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            EnsureLot, new { ItemId = itemId, LotNo = lotNo, ReceivedDt = receivedDt, User = user }, transaction, cancellationToken: cancellationToken));
        return (await FindLotAsync(itemId, null, lotNo, transaction, cancellationToken))!;
    }

    public Task<int> GetLastAutoLotSeqAsync(long itemId, string prefix, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
            SelectLastAutoLotSeq, new { ItemId = itemId, Prefix = prefix }, transaction, cancellationToken: cancellationToken));

    private sealed record ItemRow(
        long ItemId, string ItemCd, string? Type, decimal OnhandQty, decimal StkVal, decimal MvgCost, bool LotManaged, DateOnly? LotManagedFrom,
        bool LocationManaged);

    private sealed record BalanceRow(decimal Opening, DateOnly? DocumentDt, decimal? Balance, bool? MonthEnd);

    private sealed record OnHandDbRow(long ItemId, long WarehouseId, string WarehouseCode);

    private sealed record MovementRow(
        long Id, string DocumentNo, DateOnly DocumentDt, string MovType, long ItemId, long WarehouseId, decimal Qty, decimal Prc,
        string? RefKey, int? RefSeq, long? ParentMovementId, bool NegativeStock, long? SetItemId, long? LotId, decimal Amount,
        long? LocationId, bool Reversed);
}

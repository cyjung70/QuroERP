using System.Data;
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
               coalesce(onhand_qty, 0) AS onhand_qty, coalesce(stk_val, 0) AS stk_val, coalesce(mvg_cost, 0) AS mvg_cost
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
                                       ref_key, ref_seq, parent_movement_id, negative_stock, created_by, updated_by)
        VALUES (@DocumentNo, @DocumentDt, @MovType, @ItemId, @WarehouseId, @Qty, @Prc, @Amount,
                @RefKey, @RefSeq, @ParentMovementId, @NegativeStock, @User, @User)
        RETURNING id
        """;

    private const string LockMovements = """
        SELECT m.id, m.document_no, m.document_dt, m.mov_type, m.item_id, m.warehouse_id, m.qty, m.prc,
               m.ref_key, m.ref_seq, m.parent_movement_id, m.negative_stock,
               EXISTS (SELECT 1 FROM mm_goods_movement r WHERE r.parent_movement_id = m.id) AS reversed
        FROM mm_goods_movement m
        WHERE m.id = ANY(@MovementIds)
        ORDER BY m.id
        FOR UPDATE OF m
        """;

    public async Task<string?> GetCloseYearMonthAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(SelectCloseYearMonth, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<ItemStock>> LockItemsAsync(IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await transaction.Connection!.QueryAsync<ItemRow>(
            new CommandDefinition(LockItems, new { ItemIds = itemIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        return rows.Select(r => new ItemStock(r.ItemId, r.ItemCd, r.Type, r.OnhandQty, r.StkVal, r.MvgCost)).ToList();
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
                movement.DocumentNo, movement.DocumentDt, movement.MovType, movement.ItemId, movement.WarehouseId,
                movement.Qty, movement.Prc, movement.Amount, movement.RefKey, movement.RefSeq,
                movement.ParentMovementId, movement.NegativeStock, User = user,
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SavedMovement>> LockMovementsAsync(
        IReadOnlyCollection<long> movementIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await transaction.Connection!.QueryAsync<MovementRow>(
            new CommandDefinition(LockMovements, new { MovementIds = movementIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        return rows.Select(r => new SavedMovement(
            GoodsMovement.Restore(r.Id, r.DocumentNo, r.MovType, r.DocumentDt, r.ItemId, r.WarehouseId, r.Qty, r.Prc,
                r.RefKey, r.RefSeq, r.ParentMovementId, r.NegativeStock),
            r.Reversed)).ToList();
    }

    private sealed record ItemRow(long ItemId, string ItemCd, string? Type, decimal OnhandQty, decimal StkVal, decimal MvgCost);

    private sealed record OnHandDbRow(long ItemId, long WarehouseId, string WarehouseCode, decimal OnhandQty);

    private sealed record MovementRow(
        long Id, string DocumentNo, DateOnly DocumentDt, string MovType, long ItemId, long WarehouseId, decimal Qty, decimal Prc,
        string? RefKey, int? RefSeq, long? ParentMovementId, bool NegativeStock, bool Reversed);
}

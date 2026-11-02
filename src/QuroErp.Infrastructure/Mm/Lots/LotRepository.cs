using System.Data;
using Dapper;
using QuroErp.Application.Mm.Lots;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Infrastructure.Mm.Lots;

internal sealed class LotRepository : ILotRepository
{
    // MM-BR-362: balance at the end of @AsOf = current balance minus the movements dated after it
    private const string SelectBalances = """
        SELECT lot_id, lot_no, onhand_qty
        FROM (
          SELECT l.id AS lot_id, l.lot_no,
                 o.onhand_qty - coalesce((SELECT sum(m.qty) FROM mm_goods_movement m
                                          WHERE m.item_id = o.item_id AND m.warehouse_id = o.warehouse_id AND m.lot_id = o.lot_id
                                            AND m.document_dt > @AsOf), 0) AS onhand_qty
          FROM mm_lot_onhand o
          JOIN mm_lot l ON l.id = o.lot_id
          WHERE o.item_id = @ItemId AND o.warehouse_id = @WarehouseId
        ) b
        WHERE onhand_qty > 0
        ORDER BY lot_no
        """;

    private const string SelectHasNegativeOnHand = "SELECT EXISTS (SELECT 1 FROM mm_onhand WHERE item_id = @ItemId AND onhand_qty < 0)";

    private const string SelectIsSetComponent = "SELECT EXISTS (SELECT 1 FROM sd_bom WHERE child_item_id = @ItemId)";

    private const string SelectHasLotStock = "SELECT EXISTS (SELECT 1 FROM mm_lot_onhand WHERE item_id = @ItemId AND onhand_qty <> 0)";

    // MM-BR-351: each warehouse's stock is added to MIGRATED; the caller checked that no warehouse is below zero.
    // Without stock no lot is made, so an item that never moved stays deletable (MM-BR-807)
    private const string MigrateStock = $"""
        INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
        SELECT @ItemId, '{LotRules.Migrated}', @User, @User
        WHERE EXISTS (SELECT 1 FROM mm_onhand WHERE item_id = @ItemId AND onhand_qty > 0)
        ON CONFLICT (item_id, lot_no) DO NOTHING;

        INSERT INTO mm_lot_onhand (item_id, warehouse_id, lot_id, onhand_qty, created_by, updated_by)
        SELECT o.item_id, o.warehouse_id, l.id, o.onhand_qty, @User, @User
        FROM mm_onhand o
        JOIN mm_lot l ON l.item_id = o.item_id AND l.lot_no = '{LotRules.Migrated}'
        WHERE o.item_id = @ItemId AND o.onhand_qty > 0
        ON CONFLICT (item_id, warehouse_id, lot_id)
        DO UPDATE SET onhand_qty = mm_lot_onhand.onhand_qty + EXCLUDED.onhand_qty, updated_at = now(), updated_by = EXCLUDED.updated_by;
        """;

    // MM-BR-354: lot-managed items only; a warehouse with lot rows always has its mm_onhand row
    private const string SelectLotMismatches = """
        SELECT o.item_id, i.item_cd, o.warehouse_id, w.warehouse_code, o.onhand_qty, coalesce(l.lot_qty, 0) AS lot_qty
        FROM mm_onhand o
        JOIN bc_item i ON i.id = o.item_id AND i.lot_managed
        JOIN bc_warehouse w ON w.id = o.warehouse_id
        LEFT JOIN (SELECT item_id, warehouse_id, sum(onhand_qty) AS lot_qty FROM mm_lot_onhand GROUP BY item_id, warehouse_id) l
          ON l.item_id = o.item_id AND l.warehouse_id = o.warehouse_id
        WHERE o.onhand_qty <> coalesce(l.lot_qty, 0)
        ORDER BY i.item_cd, w.warehouse_code
        """;

    public async Task<IReadOnlyList<LotBalanceRow>> FindBalancesAsync(
        long itemId, long warehouseId, DateOnly asOf, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<LotBalanceRow>(new CommandDefinition(
            SelectBalances, new { ItemId = itemId, WarehouseId = warehouseId, AsOf = asOf }, transaction, cancellationToken: cancellationToken))).ToList();

    public Task<bool> HasNegativeOnHandAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Exists(SelectHasNegativeOnHand, itemId, transaction, cancellationToken);

    public Task<bool> IsSetComponentAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Exists(SelectIsSetComponent, itemId, transaction, cancellationToken);

    public Task<bool> HasLotStockAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Exists(SelectHasLotStock, itemId, transaction, cancellationToken);

    public Task MigrateStockAsync(long itemId, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MigrateStock, new { ItemId = itemId, User = user }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<LotMismatchRow>> FindLotMismatchesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<LotMismatchRow>(
            new CommandDefinition(SelectLotMismatches, transaction: transaction, cancellationToken: cancellationToken))).ToList();

    private static Task<bool> Exists(string sql, long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { ItemId = itemId }, transaction, cancellationToken: cancellationToken));
}

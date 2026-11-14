using System.Data;
using Dapper;
using QuroErp.Application.Mm.Lots;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Infrastructure.Mm.Inventory;

namespace QuroErp.Infrastructure.Mm.Lots;

internal sealed class LotRepository : ILotRepository
{
    // MM-BR-351: a balance on a lot other than '*'
    private const string SelectHasLotStock = """
        SELECT EXISTS (SELECT 1 FROM mm_onhand o JOIN mm_lot l ON l.id = o.lot_id
                       WHERE o.item_id = @ItemId AND l.lot_no <> '*' AND o.onhand_qty <> 0)
        """;

    // MM-BR-718: a balance on a Location other than '*'
    private const string SelectHasLocationStock = """
        SELECT EXISTS (SELECT 1 FROM mm_onhand o JOIN bc_location c ON c.id = o.location_id
                       WHERE o.item_id = @ItemId AND c.location_code <> '*' AND o.onhand_qty <> 0)
        """;

    // MM-BR-605, MM-BR-354: every key of mm_onhand or of the ledger whose two sums differ
    private const string SelectOnHandMismatches = """
        WITH ledger AS (
          SELECT item_id, warehouse_id, location_id, lot_id, sum(qty) AS qty
          FROM mm_goods_movement
          GROUP BY item_id, warehouse_id, location_id, lot_id
        ), diff AS (
          SELECT coalesce(o.item_id, g.item_id) AS item_id, coalesce(o.warehouse_id, g.warehouse_id) AS warehouse_id,
                 coalesce(o.location_id, g.location_id) AS location_id, coalesce(o.lot_id, g.lot_id) AS lot_id,
                 coalesce(o.onhand_qty, 0) AS onhand_qty, coalesce(g.qty, 0) AS ledger_qty
          FROM mm_onhand o
          FULL JOIN ledger g
            ON g.item_id = o.item_id AND g.warehouse_id = o.warehouse_id AND g.location_id = o.location_id AND g.lot_id = o.lot_id
        )
        SELECT d.item_id, i.item_cd, d.warehouse_id, w.warehouse_code, c.location_code, l.lot_no, d.onhand_qty, d.ledger_qty
        FROM diff d
        JOIN bc_item i ON i.id = d.item_id
        JOIN bc_warehouse w ON w.id = d.warehouse_id
        JOIN bc_location c ON c.id = d.location_id
        JOIN mm_lot l ON l.id = d.lot_id
        WHERE d.onhand_qty <> d.ledger_qty
        ORDER BY i.item_cd, w.warehouse_code, c.location_code, l.lot_no
        """;

    public async Task<IReadOnlyList<StockBalance>> FindBalancesAsync(
        long itemId, long warehouseId, DateOnly asOf, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<StockBalance>(new CommandDefinition(
            StockBalanceSql.SelectBalances, new { ItemId = itemId, WarehouseId = warehouseId, AsOf = asOf }, transaction, cancellationToken: cancellationToken))).ToList();


    public Task<bool> HasLotStockAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Exists(SelectHasLotStock, itemId, transaction, cancellationToken);

    public Task<bool> HasLocationStockAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Exists(SelectHasLocationStock, itemId, transaction, cancellationToken);

    public async Task<IReadOnlyList<OnHandMismatchRow>> FindOnHandMismatchesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<OnHandMismatchRow>(
            new CommandDefinition(SelectOnHandMismatches, transaction: transaction, cancellationToken: cancellationToken))).ToList();

    private static Task<bool> Exists(string sql, long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { ItemId = itemId }, transaction, cancellationToken: cancellationToken));
}

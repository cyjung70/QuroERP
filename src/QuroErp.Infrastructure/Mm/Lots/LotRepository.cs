using System.Data;
using Dapper;
using QuroErp.Application.Mm.Lots;

namespace QuroErp.Infrastructure.Mm.Lots;

internal sealed class LotRepository : ILotRepository
{
    // MM-BR-362: balance at the end of @AsOf = current balance minus the movements dated after it. A lot balance is the sum of
    // the item + warehouse rows of that lot in mm_onhand (MM-BR-354); Lot '*' holds the stock from before lot management
    private const string SelectBalances = """
        SELECT lot_id, lot_no, onhand_qty
        FROM (
          SELECT l.id AS lot_id, l.lot_no,
                 o.onhand_qty - coalesce((SELECT sum(m.qty) FROM mm_goods_movement m
                                          WHERE m.item_id = o.item_id AND m.warehouse_id = o.warehouse_id AND m.lot_id = o.lot_id
                                            AND m.document_dt > @AsOf), 0) AS onhand_qty
          FROM (SELECT item_id, warehouse_id, lot_id, sum(onhand_qty) AS onhand_qty FROM mm_onhand
                WHERE item_id = @ItemId AND warehouse_id = @WarehouseId GROUP BY item_id, warehouse_id, lot_id) o
          JOIN mm_lot l ON l.id = o.lot_id
        ) b
        WHERE onhand_qty > 0
        ORDER BY lot_no
        """;

    private const string SelectHasNegativeOnHand = "SELECT EXISTS (SELECT 1 FROM mm_onhand WHERE item_id = @ItemId AND onhand_qty < 0)";

    private const string SelectIsSetComponent = "SELECT EXISTS (SELECT 1 FROM sd_bom WHERE child_item_id = @ItemId)";

    // MM-BR-351: a balance on a lot other than '*'
    private const string SelectHasLotStock = """
        SELECT EXISTS (SELECT 1 FROM mm_onhand o JOIN mm_lot l ON l.id = o.lot_id
                       WHERE o.item_id = @ItemId AND l.lot_no <> '*' AND o.onhand_qty <> 0)
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

    // MM-BR-351: the stock from before lot management stays on Lot '*', where it already is (TASK-CORE-067)
    public Task MigrateStockAsync(long itemId, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    // MM-BR-354: lot balances are rows of mm_onhand, so an item + warehouse balance is their sum and cannot differ from them.
    // The reconciliation compares the ledger per key (MM-BR-605, TASK-CORE-068)
    public Task<IReadOnlyList<LotMismatchRow>> FindLotMismatchesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LotMismatchRow>>([]);

    private static Task<bool> Exists(string sql, long itemId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { ItemId = itemId }, transaction, cancellationToken: cancellationToken));
}

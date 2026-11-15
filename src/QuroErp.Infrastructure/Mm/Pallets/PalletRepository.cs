using System.Data;
using Dapper;
using QuroErp.Application.Mm.Pallets;

namespace QuroErp.Infrastructure.Mm.Pallets;

internal sealed class PalletRepository : IPalletRepository
{
    // MM-BR-910, MM-BR-916: the Pallet and its CREATE event with its qty
    private const string Insert = """
        WITH pallet AS (
          INSERT INTO mm_pallet (pallet_no, item_id, lot_id, warehouse_id, location_id, qty, goods_receipt_line_id, created_by, updated_by)
          VALUES (@PalletNo, @ItemId, @LotId, @WarehouseId, @LocationId, @Qty, @GoodsReceiptLineId, @User, @User)
          RETURNING id
        )
        INSERT INTO mm_pallet_event (pallet_id, event_type, event_dt, qty, ref_key, ref_seq, created_by, updated_by)
        SELECT id, 'CREATE', @EventDt, @Qty, @RefKey, @RefSeq, @User, @User FROM pallet
        """;

    // MM-BR-911: as received = ACTIVE on the line's warehouse and Location with its CREATE qty, and nothing else happened to it
    private const string LockByReceiptLines = """
        SELECT p.id, p.pallet_no, p.goods_receipt_line_id,
               coalesce(p.status = 'ACTIVE' AND p.warehouse_id = l.warehouse_id AND p.location_id = l.location_id
                        AND p.qty = (SELECT e.qty FROM mm_pallet_event e
                                     WHERE e.pallet_id = p.id AND e.event_type = 'CREATE' ORDER BY e.id LIMIT 1)
                        AND NOT EXISTS (SELECT 1 FROM mm_pallet_event e WHERE e.pallet_id = p.id AND e.event_type <> 'CREATE')
                        AND NOT EXISTS (SELECT 1 FROM mm_pallet_event e WHERE e.related_pallet_id = p.id), false) AS as_received
        FROM mm_pallet p JOIN mm_goods_receipt_line l ON l.id = p.goods_receipt_line_id
        WHERE p.goods_receipt_line_id = ANY(@Ids)
        ORDER BY p.pallet_no
        FOR UPDATE OF p
        """;

    // The ADJUST event takes the old qty: the CTEs and the UPDATE read the same snapshot
    private const string CloseForDeletedLines = """
        WITH event AS (
          INSERT INTO mm_pallet_event (pallet_id, event_type, event_dt, qty, ref_key, created_by, updated_by)
          SELECT id, 'ADJUST', @EventDt, -qty, @RefKey, @User, @User FROM mm_pallet WHERE id = ANY(@Ids)
        )
        UPDATE mm_pallet
        SET qty = 0, status = 'CLOSED', goods_receipt_line_id = NULL, updated_at = now(), updated_by = @User
        WHERE id = ANY(@Ids)
        """;

    private const string SelectNumbersByReceipt = """
        SELECT p.goods_receipt_line_id, p.pallet_no
        FROM mm_pallet p JOIN mm_goods_receipt_line l ON l.id = p.goods_receipt_line_id
        WHERE l.goods_receipt_id = @Id
        ORDER BY p.pallet_no
        """;

    private const string SelectReceiptLabels = """
        SELECT p.id, p.pallet_no, coalesce(i.item_cd, '') AS item_cd, i.item_name, lot.lot_no, p.qty, lot.expiry_dt
        FROM mm_pallet p
        JOIN mm_goods_receipt_line l ON l.id = p.goods_receipt_line_id
        JOIN bc_item i ON i.id = p.item_id
        JOIN mm_lot lot ON lot.id = p.lot_id
        WHERE l.goods_receipt_id = @Id AND p.status = 'ACTIVE'
        ORDER BY l.seq, p.pallet_no
        """;

    public Task InsertAsync(IReadOnlyList<NewPallet> pallets, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert,
            pallets.Select(p => new
            {
                p.PalletNo,
                p.ItemId,
                p.LotId,
                p.WarehouseId,
                p.LocationId,
                p.Qty,
                p.GoodsReceiptLineId,
                p.EventDt,
                p.RefKey,
                p.RefSeq,
                User = user,
            }).ToList(),
            transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<ReceiptPalletRow>> LockByReceiptLinesAsync(
        IReadOnlyCollection<long> goodsReceiptLineIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<ReceiptPalletRow>(new CommandDefinition(
            LockByReceiptLines, new { Ids = goodsReceiptLineIds.ToArray() }, transaction, cancellationToken: cancellationToken))];

    public Task CloseForDeletedLinesAsync(
        IReadOnlyCollection<long> palletIds, DateOnly eventDt, string refKey, string user, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            CloseForDeletedLines, new { Ids = palletIds.ToArray(), EventDt = eventDt, RefKey = refKey, User = user }, transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> FindNumbersByReceiptAsync(
        long goodsReceiptId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long LineId, string PalletNo)>(new CommandDefinition(
            SelectNumbersByReceipt, new { Id = goodsReceiptId }, transaction, cancellationToken: cancellationToken)))
        .GroupBy(r => r.LineId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(r => r.PalletNo)]);

    public async Task<IReadOnlyList<PalletLabelRow>> FindReceiptLabelsAsync(
        long goodsReceiptId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<PalletLabelRow>(new CommandDefinition(
            SelectReceiptLabels, new { Id = goodsReceiptId }, transaction, cancellationToken: cancellationToken))];
}

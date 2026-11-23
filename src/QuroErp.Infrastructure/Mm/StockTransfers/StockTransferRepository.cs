using System.Data;
using Dapper;
using QuroErp.Application.Mm.StockTransfers;
using QuroErp.Contracts.Mm.StockTransfers;

namespace QuroErp.Infrastructure.Mm.StockTransfers;

internal sealed class StockTransferRepository : IStockTransferRepository
{
    // LineCount and Amt come from the lines
    private const string SelectRows = """
        SELECT t.id, t.transfer_no, t.mov_type, t.transfer_dt, t.from_warehouse_id, fw.warehouse_code AS from_warehouse_code,
               t.to_warehouse_id, tw.warehouse_code AS to_warehouse_code,
               (SELECT count(*)::int FROM mm_stock_transfer_line l WHERE l.stock_transfer_id = t.id) AS line_count,
               coalesce((SELECT sum(l.amt) FROM mm_stock_transfer_line l WHERE l.stock_transfer_id = t.id), 0) AS amt,
               t.remarks, t.updated_at
        FROM mm_stock_transfer t
        JOIN bc_warehouse fw ON fw.id = t.from_warehouse_id
        JOIN bc_warehouse tw ON tw.id = t.to_warehouse_id
        """;

    private const string Search = SelectRows + """

        WHERE (@From::date IS NULL OR t.transfer_dt >= @From)
          AND (@To::date IS NULL OR t.transfer_dt <= @To)
          AND (@TransferNo::varchar IS NULL OR t.transfer_no LIKE @TransferNo || '%')
          AND (@WarehouseId::bigint IS NULL OR t.from_warehouse_id = @WarehouseId OR t.to_warehouse_id = @WarehouseId)
          AND (@MovType::varchar IS NULL OR t.mov_type = @MovType)
        ORDER BY t.transfer_dt DESC, t.id DESC
        LIMIT @Limit
        """;

    private const string FindById = SelectRows + "\nWHERE t.id = @Id";

    private const string LockRow = "SELECT id FROM mm_stock_transfer WHERE id = @Id FOR UPDATE";

    // FromOnHandQty: the item + sending warehouse stock now (MM-BR-714, the sum of its keys)
    private const string SelectLines = """
        SELECT l.id, l.seq, l.item_id, i.item_cd, i.item_name, i.unit, l.qty, l.lot_id, lot.lot_no, l.from_location_id,
               fl.location_code AS from_location_code, l.to_location_id, tl.location_code AS to_location_code, l.pallet_id, p.pallet_no,
               l.prc, l.amt,
               coalesce((SELECT sum(o.onhand_qty) FROM mm_onhand o WHERE o.item_id = l.item_id AND o.warehouse_id = t.from_warehouse_id), 0)
                   AS from_on_hand_qty
        FROM mm_stock_transfer_line l
        JOIN mm_stock_transfer t ON t.id = l.stock_transfer_id
        JOIN bc_item i ON i.id = l.item_id
        JOIN mm_lot lot ON lot.id = l.lot_id
        JOIN bc_location fl ON fl.id = l.from_location_id
        JOIN bc_location tl ON tl.id = l.to_location_id
        LEFT JOIN mm_pallet p ON p.id = l.pallet_id
        WHERE l.stock_transfer_id = @Id
        ORDER BY l.seq
        """;

    private const string SelectWarehouses = "SELECT id, warehouse_code, use_yn AS in_use FROM bc_warehouse WHERE id = ANY(@Ids)";

    private const string Insert = """
        INSERT INTO mm_stock_transfer (transfer_no, mov_type, transfer_dt, from_warehouse_id, to_warehouse_id, remarks, created_by, updated_by)
        VALUES (@TransferNo, @MovType, @TransferDt, @FromWarehouseId, @ToWarehouseId, @Remarks, @User, @User)
        RETURNING id
        """;

    private const string Update = """
        UPDATE mm_stock_transfer
        SET mov_type = @MovType, transfer_dt = @TransferDt, from_warehouse_id = @FromWarehouseId, to_warehouse_id = @ToWarehouseId,
            remarks = @Remarks, updated_at = now(), updated_by = @User
        WHERE id = @Id AND updated_at = @UpdatedAt
        """;

    private const string DeleteLines = "DELETE FROM mm_stock_transfer_line WHERE stock_transfer_id = @Id";

    private const string InsertLine = """
        INSERT INTO mm_stock_transfer_line (stock_transfer_id, seq, item_id, qty, lot_id, from_location_id, to_location_id, pallet_id, prc, amt,
                                            created_by, updated_by)
        VALUES (@TransferId, @Seq, @ItemId, @Qty, @LotId, @FromLocationId, @ToLocationId, @PalletId, @Prc, @Amt, @User, @User)
        """;

    private const string Delete = "DELETE FROM mm_stock_transfer WHERE id = @Id AND updated_at = @UpdatedAt";

    private const string SelectOpenMovements = """
        SELECT m.id
        FROM mm_goods_movement m
        WHERE m.ref_key = @RefKey
          AND m.parent_movement_id IS NULL
          AND NOT EXISTS (SELECT 1 FROM mm_goods_movement r WHERE r.parent_movement_id = m.id)
        ORDER BY m.id
        """;

    private const string MovePallet = """
        UPDATE mm_pallet SET warehouse_id = @WarehouseId, location_id = @LocationId, updated_at = now(), updated_by = @User WHERE id = @PalletId;
        INSERT INTO mm_pallet_event (pallet_id, event_type, event_dt, qty, ref_key, ref_seq, created_by, updated_by)
        SELECT id, 'MOVE', @EventDt, qty, @RefKey, @RefSeq, @User, @User FROM mm_pallet WHERE id = @PalletId;
        """;

    private const string SelectScannedLocation = """
        SELECT w.id AS warehouse_id, w.warehouse_code, w.use_yn AS warehouse_in_use, c.id AS location_id, c.location_code,
               c.use_yn AS location_in_use
        FROM bc_warehouse w
        JOIN bc_location c ON c.warehouse_id = w.id
        WHERE w.warehouse_code = @WarehouseCode AND c.location_code = @LocationCode
        """;

    private const string SelectScannedPallet = """
        SELECT p.id, p.pallet_no, p.item_id, i.item_cd, i.item_name, p.lot_id, lot.lot_no, p.qty, p.status,
               w.id AS warehouse_id, w.warehouse_code, w.use_yn AS warehouse_in_use, c.id AS location_id, c.location_code,
               c.use_yn AS location_in_use
        FROM mm_pallet p
        JOIN bc_item i ON i.id = p.item_id
        JOIN mm_lot lot ON lot.id = p.lot_id
        JOIN bc_warehouse w ON w.id = p.warehouse_id
        JOIN bc_location c ON c.id = p.location_id
        WHERE p.pallet_no = @PalletNo
        """;

    public async Task<IReadOnlyList<StockTransferRow>> SearchAsync(
        StockTransferFilter filter, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await transaction.Connection!.QueryAsync<StoredRow>(new CommandDefinition(
            Search, new { filter.From, filter.To, filter.TransferNo, filter.WarehouseId, filter.MovType, filter.Limit }, transaction,
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => r.ToRow())];
    }

    public async Task<StockTransferRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (forUpdate && await transaction.Connection!.ExecuteScalarAsync<long?>(new CommandDefinition(
                LockRow, new { Id = id }, transaction, cancellationToken: cancellationToken)) is null)
        {
            return null;
        }
        return (await transaction.Connection!.QuerySingleOrDefaultAsync<StoredRow>(new CommandDefinition(
            FindById, new { Id = id }, transaction, cancellationToken: cancellationToken)))?.ToRow();
    }

    public async Task<IReadOnlyList<StockTransferLineDto>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<StockTransferLineDto>(new CommandDefinition(
            SelectLines, new { Id = id }, transaction, cancellationToken: cancellationToken))];

    public async Task<IReadOnlyList<TransferWarehouse>> FindWarehousesAsync(
        IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<TransferWarehouse>(new CommandDefinition(
            SelectWarehouses, new { Ids = ids.Distinct().ToArray() }, transaction, cancellationToken: cancellationToken))];

    public Task<long> InsertAsync(
        string transferNo, StockTransferHeader header, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert,
            new { TransferNo = transferNo, header.MovType, header.TransferDt, header.FromWarehouseId, header.ToWarehouseId, header.Remarks, User = user },
            transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, StockTransferHeader header, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var updated = await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                Id = id,
                header.MovType,
                header.TransferDt,
                header.FromWarehouseId,
                header.ToWarehouseId,
                header.Remarks,
                UpdatedAt = updatedAt,
                User = user,
            },
            transaction, cancellationToken: cancellationToken));
        if (updated != 1)
        {
            return false;
        }
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(DeleteLines, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return true;
    }

    public Task InsertLinesAsync(
        long id, IReadOnlyList<NewStockTransferLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            InsertLine,
            lines.Select(l => new
            {
                TransferId = id,
                l.Seq,
                l.ItemId,
                l.Qty,
                l.LotId,
                l.FromLocationId,
                l.ToLocationId,
                l.PalletId,
                l.Prc,
                l.Amt,
                User = user,
            }),
            transaction, cancellationToken: cancellationToken));

    public async Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Id = id, UpdatedAt = updatedAt }, transaction, cancellationToken: cancellationToken)) == 1;

    public async Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(string transferNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            SelectOpenMovements, new { RefKey = transferNo }, transaction, cancellationToken: cancellationToken))];

    public Task<ScannedLocation?> FindScannedLocationAsync(
        string warehouseCode, string locationCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<ScannedLocation?>(new CommandDefinition(
            SelectScannedLocation, new { WarehouseCode = warehouseCode, LocationCode = locationCode }, transaction,
            cancellationToken: cancellationToken));

    public Task<ScannedPallet?> FindScannedPalletAsync(string palletNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<ScannedPallet?>(new CommandDefinition(
            SelectScannedPallet, new { PalletNo = palletNo }, transaction, cancellationToken: cancellationToken));

    public Task MovePalletAsync(
        long palletId, long warehouseId, long locationId, DateOnly eventDt, string refKey, int refSeq, string user, IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            MovePallet,
            new
            {
                PalletId = palletId,
                WarehouseId = warehouseId,
                LocationId = locationId,
                EventDt = eventDt,
                RefKey = refKey,
                RefSeq = refSeq,
                User = user,
            },
            transaction, cancellationToken: cancellationToken));

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record StoredRow(
        long Id, string TransferNo, string MovType, DateOnly TransferDt, long FromWarehouseId, string FromWarehouseCode, long ToWarehouseId,
        string ToWarehouseCode, int LineCount, decimal Amt, string? Remarks, DateTime UpdatedAt)
    {
        public StockTransferRow ToRow() =>
            new(Id, TransferNo, MovType, TransferDt, FromWarehouseId, FromWarehouseCode, ToWarehouseId, ToWarehouseCode, LineCount, Amt, Remarks,
                new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)));
    }
}

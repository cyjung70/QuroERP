using System.Data;
using Dapper;
using QuroErp.Application.Sd.Assemblies;
using QuroErp.Contracts.Sd.Assemblies;

namespace QuroErp.Infrastructure.Sd.Assemblies;

internal sealed class AssemblyRepository : IAssemblyRepository
{
    // Amt is the sum of the lines; QtyLeft is the assembly qty minus its open disassemblies, 0 for anything else
    private const string SelectRows = """
        SELECT a.id, a.assembly_no, a.assembly_kind AS kind, a.assembly_dt, a.set_item_id, i.item_cd AS set_item_cd,
               i.item_name AS set_item_name, a.qty, a.warehouse_id, w.warehouse_code, a.location_id, loc.location_code,
               a.set_lot_id, lot.lot_no AS set_lot_no, a.source_assembly_id, s.assembly_no AS source_assembly_no, a.status,
               coalesce((SELECT sum(l.amt) FROM mm_assembly_line l WHERE l.assembly_id = a.id), 0) AS amt,
               CASE WHEN a.assembly_kind = 'ASSEMBLE' AND a.status = 'OP'
                    THEN a.qty - coalesce((SELECT sum(d.qty) FROM mm_assembly d WHERE d.source_assembly_id = a.id AND d.status = 'OP'), 0)
                    ELSE 0 END AS qty_left,
               a.remarks, a.updated_at
        FROM mm_assembly a
        JOIN bc_item i ON i.id = a.set_item_id
        JOIN bc_warehouse w ON w.id = a.warehouse_id
        JOIN bc_location loc ON loc.id = a.location_id
        LEFT JOIN mm_lot lot ON lot.id = a.set_lot_id
        LEFT JOIN mm_assembly s ON s.id = a.source_assembly_id
        """;

    private const string Search = SelectRows + """

        WHERE (@From::date IS NULL OR a.assembly_dt >= @From)
          AND (@To::date IS NULL OR a.assembly_dt <= @To)
          AND (@SetItemId::bigint IS NULL OR a.set_item_id = @SetItemId)
          AND (@Kind::varchar IS NULL OR a.assembly_kind = @Kind)
        ORDER BY a.assembly_dt DESC, a.id DESC
        LIMIT @Limit
        """;

    private const string FindById = SelectRows + "\nWHERE a.id = @Id";

    private const string LockRow = "SELECT id FROM mm_assembly WHERE id = @Id FOR UPDATE";

    private const string SelectLines = """
        SELECT l.seq, l.item_id, i.item_cd, i.item_name, l.qty, l.location_id, loc.location_code, l.lot_id, lot.lot_no, l.prc, l.amt
        FROM mm_assembly_line l
        JOIN bc_item i ON i.id = l.item_id
        JOIN bc_location loc ON loc.id = l.location_id
        JOIN mm_lot lot ON lot.id = l.lot_id
        WHERE l.assembly_id = @Id
        ORDER BY l.seq
        """;

    private const string SelectItems = """
        SELECT id, item_cd, item_name, type, coalesce(valid_flag, 'Y') <> 'N' AS in_use, coalesce(mvg_cost, 0) AS moving_average_cost
        FROM bc_item
        WHERE id = ANY(@Ids)
        """;

    private const string Insert = """
        INSERT INTO mm_assembly (assembly_no, assembly_kind, assembly_dt, set_item_id, qty, warehouse_id, location_id, set_lot_id,
                                 source_assembly_id, status, remarks, created_by, updated_by)
        VALUES (@AssemblyNo, @Kind, @AssemblyDt, @SetItemId, @Qty, @WarehouseId, @LocationId, @SetLotId, @SourceAssemblyId, 'OP',
                @Remarks, @User, @User)
        RETURNING id
        """;

    private const string SetSetLot = """
        UPDATE mm_assembly SET set_lot_id = @SetLotId, updated_at = now(), updated_by = @User WHERE id = @Id;
        UPDATE mm_lot SET assembly_id = @Id, updated_at = now(), updated_by = @User WHERE id = @SetLotId AND @MadeByAssembly;
        """;

    private const string InsertLine = """
        INSERT INTO mm_assembly_line (assembly_id, seq, item_id, qty, warehouse_id, location_id, lot_id, prc, amt, created_by, updated_by)
        VALUES (@AssemblyId, @Seq, @ItemId, @Qty, @WarehouseId, @LocationId, @LotId, @Prc, @Amt, @User, @User)
        """;

    private const string InsertLotLink = """
        INSERT INTO mm_lot_link (set_lot_id, component_lot_id, assembly_id, qty, created_by, updated_by)
        VALUES (@SetLotId, @ComponentLotId, @AssemblyId, @Qty, @User, @User)
        """;

    private const string SelectReturned = """
        SELECT l.item_id, sum(l.qty) AS qty
        FROM mm_assembly d
        JOIN mm_assembly_line l ON l.assembly_id = d.id
        WHERE d.source_assembly_id = @Id AND d.status = 'OP'
        GROUP BY l.item_id
        """;

    private const string SelectOpenMovements = """
        SELECT m.id
        FROM mm_goods_movement m
        WHERE m.ref_key = @RefKey
          AND m.parent_movement_id IS NULL
          AND NOT EXISTS (SELECT 1 FROM mm_goods_movement r WHERE r.parent_movement_id = m.id)
        ORDER BY m.id
        """;

    private const string Void = "UPDATE mm_assembly SET status = 'VO', updated_at = now(), updated_by = @User WHERE id = @Id";

    public async Task<IReadOnlyList<AssemblyRow>> SearchAsync(AssemblyFilter filter, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await transaction.Connection!.QueryAsync<StoredRow>(new CommandDefinition(
            Search, new { filter.From, filter.To, filter.SetItemId, filter.Kind, filter.Limit }, transaction, cancellationToken: cancellationToken));
        return [.. rows.Select(r => r.ToRow())];
    }

    public async Task<AssemblyRow?> FindAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QuerySingleOrDefaultAsync<StoredRow>(new CommandDefinition(
            FindById, new { Id = id }, transaction, cancellationToken: cancellationToken)))?.ToRow();

    public async Task<AssemblyRow?> LockAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var locked = await transaction.Connection!.ExecuteScalarAsync<long?>(new CommandDefinition(
            LockRow, new { Id = id }, transaction, cancellationToken: cancellationToken));
        return locked is null ? null : await FindAsync(id, transaction, cancellationToken);
    }

    public async Task<IReadOnlyList<AssemblyLineDto>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<AssemblyLineDto>(new CommandDefinition(
            SelectLines, new { Id = id }, transaction, cancellationToken: cancellationToken))];

    public async Task<IReadOnlyList<AssemblyItem>> FindItemsAsync(
        IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<AssemblyItem>(new CommandDefinition(
            SelectItems, new { Ids = itemIds.ToArray() }, transaction, cancellationToken: cancellationToken))];

    public Task<string?> FindWarehouseCodeAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT warehouse_code FROM bc_warehouse WHERE id = @Id", new { Id = warehouseId }, transaction, cancellationToken: cancellationToken));

    public Task<long> InsertAsync(NewAssembly assembly, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert,
            new
            {
                assembly.AssemblyNo,
                assembly.Kind,
                assembly.AssemblyDt,
                assembly.SetItemId,
                assembly.Qty,
                assembly.WarehouseId,
                assembly.LocationId,
                assembly.SetLotId,
                assembly.SourceAssemblyId,
                assembly.Remarks,
                User = user,
            },
            transaction, cancellationToken: cancellationToken));

    public Task SetSetLotAsync(long id, long setLotId, bool madeByAssembly, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SetSetLot, new { Id = id, SetLotId = setLotId, MadeByAssembly = madeByAssembly, User = user }, transaction, cancellationToken: cancellationToken));

    public Task InsertLinesAsync(long id, IReadOnlyList<NewAssemblyLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            InsertLine,
            lines.Select(l => new { AssemblyId = id, l.Seq, l.ItemId, l.Qty, l.WarehouseId, l.LocationId, l.LotId, l.Prc, l.Amt, User = user }),
            transaction, cancellationToken: cancellationToken));

    public Task InsertLotLinksAsync(long id, IReadOnlyList<NewLotLink> links, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            InsertLotLink,
            links.Select(l => new { l.SetLotId, l.ComponentLotId, AssemblyId = id, l.Qty, User = user }),
            transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyDictionary<long, decimal>> FindReturnedQtyAsync(long assemblyId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long ItemId, decimal Qty)>(new CommandDefinition(
            SelectReturned, new { Id = assemblyId }, transaction, cancellationToken: cancellationToken)))
        .ToDictionary(r => r.ItemId, r => r.Qty);

    public async Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(string assemblyNo, IDbTransaction transaction, CancellationToken cancellationToken) =>
        [.. await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            SelectOpenMovements, new { RefKey = assemblyNo }, transaction, cancellationToken: cancellationToken))];

    public Task VoidAsync(long id, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(Void, new { Id = id, User = user }, transaction, cancellationToken: cancellationToken));

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record StoredRow(
        long Id, string AssemblyNo, string Kind, DateOnly AssemblyDt, long SetItemId, string SetItemCd, string? SetItemName, decimal Qty,
        long WarehouseId, string WarehouseCode, long LocationId, string LocationCode, long? SetLotId, string? SetLotNo,
        long? SourceAssemblyId, string? SourceAssemblyNo, string Status, decimal Amt, decimal QtyLeft, string? Remarks, DateTime UpdatedAt)
    {
        public AssemblyRow ToRow() =>
            new(Id, AssemblyNo, Kind, AssemblyDt, SetItemId, SetItemCd, SetItemName, Qty, WarehouseId, WarehouseCode, LocationId, LocationCode,
                SetLotId, SetLotNo, SourceAssemblyId, SourceAssemblyNo, Status, Amt, QtyLeft, Remarks,
                new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)));
    }
}

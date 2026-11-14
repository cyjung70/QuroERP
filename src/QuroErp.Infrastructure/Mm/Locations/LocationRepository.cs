using System.Data;
using Dapper;
using QuroErp.Application.Mm.Locations;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Domain.Mm.Locations;

namespace QuroErp.Infrastructure.Mm.Locations;

internal sealed class LocationRepository : ILocationRepository
{
    private const string SelectWarehouseExists = "SELECT EXISTS (SELECT 1 FROM bc_warehouse WHERE id = @WarehouseId)";

    // The balance of every item on the Location together
    private const string SelectColumns = """
        SELECT l.id, l.warehouse_id, l.location_code, l.location_name, l.is_system, l.use_yn, l.remarks,
               COALESCE((SELECT sum(o.onhand_qty) FROM mm_onhand o WHERE o.location_id = l.id), 0) AS onhand_qty, l.updated_at
        FROM bc_location l
        """;

    private const string Select = SelectColumns + """

        WHERE l.warehouse_id = @WarehouseId
          AND (@IncludeInactive OR l.use_yn)
          AND (@Code::varchar IS NULL OR starts_with(l.location_code, upper(@Code::varchar)))
        ORDER BY l.is_system DESC, l.location_code
        """;

    // Every Location of the warehouses involved, so the code check sees the whole warehouse; FOR UPDATE in id order
    private const string Lock = """
        SELECT l.id, l.warehouse_id, l.location_code, l.location_name, l.is_system, l.use_yn, l.remarks, 0::numeric AS onhand_qty,
               l.updated_at
        FROM bc_location l
        WHERE l.warehouse_id = ANY(@WarehouseIds)
           OR l.warehouse_id IN (SELECT x.warehouse_id FROM bc_location x WHERE x.id = ANY(@Ids))
        ORDER BY l.id
        FOR UPDATE OF l
        """;

    private const string SelectOptions = """
        SELECT id, location_code, location_name
        FROM bc_location
        WHERE warehouse_id = @WarehouseId AND use_yn
        ORDER BY is_system DESC, location_code
        """;

    // MM-BR-717: a balance other than 0 on the Location
    private const string SelectInactivationBlockers = """
        SELECT l.id, 'ONHAND' AS reason
        FROM bc_location l
        WHERE l.id = ANY(@Ids)
          AND EXISTS (SELECT 1 FROM mm_onhand o WHERE o.location_id = l.id AND o.onhand_qty <> 0)
        """;

    // MM-BR-717: a balance blocks a delete; so do movements and any other row that refers to the Location
    private const string SelectDeleteBlockers = """
        SELECT l.id,
               CASE WHEN EXISTS (SELECT 1 FROM mm_onhand o WHERE o.location_id = l.id AND o.onhand_qty <> 0) THEN 'ONHAND'
                    ELSE 'HAS_MOVEMENT' END AS reason
        FROM bc_location l
        WHERE l.id = ANY(@Ids)
          AND (EXISTS (SELECT 1 FROM mm_onhand x WHERE x.location_id = l.id)
               OR EXISTS (SELECT 1 FROM mm_goods_movement x WHERE x.location_id = l.id)
               OR EXISTS (SELECT 1 FROM mm_goods_receipt_line x WHERE x.location_id = l.id)
               OR EXISTS (SELECT 1 FROM mm_unplanned_receipt_line x WHERE x.location_id = l.id)
               OR EXISTS (SELECT 1 FROM mm_stock_count_line x WHERE x.location_id = l.id)
               OR EXISTS (SELECT 1 FROM mm_stock_transfer_line x WHERE x.from_location_id = l.id OR x.to_location_id = l.id)
               OR EXISTS (SELECT 1 FROM sd_shipment_line x WHERE x.location_id = l.id)
               OR EXISTS (SELECT 1 FROM sd_sales_invoice_line x WHERE x.location_id = l.id))
        """;

    private const string Insert = """
        INSERT INTO bc_location (warehouse_id, location_code, location_name, is_system, use_yn, remarks, created_by, updated_by)
        VALUES (@WarehouseId, @LocationCode, @LocationName, false, @UseYn, @Remarks, @UpdatedBy, @UpdatedBy)
        """;

    private const string Update = """
        UPDATE bc_location
        SET location_name = @LocationName, use_yn = @UseYn, remarks = @Remarks, updated_at = now(), updated_by = @UpdatedBy
        WHERE id = @Id AND NOT is_system
        """;

    private const string Delete = "DELETE FROM bc_location WHERE id = ANY(@Ids) AND NOT is_system";

    public Task<bool> WarehouseExistsAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(new CommandDefinition(
            SelectWarehouseExists, new { WarehouseId = warehouseId }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<LocationRow>> FindAsync(
        long warehouseId, string? code, bool includeInactive, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ToRows(await transaction.Connection!.QueryAsync<StoredRow>(new CommandDefinition(
            Select, new { WarehouseId = warehouseId, Code = string.IsNullOrEmpty(code) ? null : code, IncludeInactive = includeInactive },
            transaction, cancellationToken: cancellationToken)));

    public async Task<IReadOnlyList<LocationRow>> LockAsync(
        IReadOnlyCollection<long> locationIds, IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (locationIds.Count == 0 && warehouseIds.Count == 0)
        {
            return [];
        }
        return ToRows(await transaction.Connection!.QueryAsync<StoredRow>(new CommandDefinition(
            Lock, new { Ids = locationIds.ToArray(), WarehouseIds = warehouseIds.ToArray() }, transaction, cancellationToken: cancellationToken)));
    }

    public async Task<IReadOnlyList<LocationOptionDto>> FindOptionsAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<LocationOptionDto>(new CommandDefinition(
            SelectOptions, new { WarehouseId = warehouseId }, transaction, cancellationToken: cancellationToken))).ToList();

    public Task<IReadOnlyDictionary<long, string>> FindInactivationBlockersAsync(
        IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ReasonsAsync(SelectInactivationBlockers, locationIds, transaction, cancellationToken);

    public Task<IReadOnlyDictionary<long, string>> FindDeleteBlockersAsync(
        IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ReasonsAsync(SelectDeleteBlockers, locationIds, transaction, cancellationToken);

    public Task InsertAsync(LocationState location, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert, Parameters(location, updatedBy), transaction, cancellationToken: cancellationToken));

    public Task UpdateAsync(LocationState location, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update, Parameters(location, updatedBy), transaction, cancellationToken: cancellationToken));

    public async Task DeleteAsync(IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (locationIds.Count == 0)
        {
            return;
        }
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Delete, new { Ids = locationIds.ToArray() }, transaction, cancellationToken: cancellationToken));
    }

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record StoredRow(
        long Id, long WarehouseId, string LocationCode, string? LocationName, bool IsSystem, bool UseYn, string? Remarks, decimal OnhandQty,
        DateTime UpdatedAt);

    private static List<LocationRow> ToRows(IEnumerable<StoredRow> rows) =>
        rows.Select(r => new LocationRow(
            r.Id, r.WarehouseId, r.LocationCode, r.LocationName, r.IsSystem, r.UseYn, r.Remarks, r.OnhandQty,
            new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))))
        .ToList();

    private static object Parameters(LocationState l, string updatedBy) => new
    {
        l.Id,
        l.WarehouseId,
        l.LocationCode,
        l.LocationName,
        l.UseYn,
        l.Remarks,
        UpdatedBy = updatedBy,
    };

    private static async Task<IReadOnlyDictionary<long, string>> ReasonsAsync(
        string sql, IReadOnlyCollection<long> locationIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (locationIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }
        var rows = await transaction.Connection!.QueryAsync<(long Id, string Reason)>(
            new CommandDefinition(sql, new { Ids = locationIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.Id, r => r.Reason);
    }
}

using System.Data;
using Dapper;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Mm.Warehouses;

namespace QuroErp.Infrastructure.Mm.Warehouses;

internal sealed class WarehouseRepository : IWarehouseRepository
{
    private const string SelectOptions = """
        SELECT id, warehouse_code, warehouse_name, warehouse_type, use_yn
        FROM bc_warehouse
        WHERE (@IncludeInactive OR use_yn) AND (@IncludeConsignment OR warehouse_type <> 'CONSIGNMENT')
        ORDER BY warehouse_code
        """;

    private const string SelectConsignment = "SELECT id, warehouse_code FROM bc_warehouse WHERE warehouse_type = 'CONSIGNMENT'";

    private const string SelectAll = """
        SELECT id, warehouse_code, warehouse_name, is_default, use_yn, warehouse_type, remarks, updated_at
        FROM bc_warehouse
        ORDER BY warehouse_code
        """;

    // MM-BR-704: a balance other than 0 today, or a movement dated after today
    private const string SelectInactivationBlockers = """
        SELECT w.id,
               CASE WHEN EXISTS (SELECT 1 FROM mm_onhand o WHERE o.warehouse_id = w.id AND o.onhand_qty <> 0) THEN 'ONHAND'
                    ELSE 'FUTURE_MOVEMENT' END AS reason
        FROM bc_warehouse w
        WHERE w.id = ANY(@Ids)
          AND (EXISTS (SELECT 1 FROM mm_onhand o WHERE o.warehouse_id = w.id AND o.onhand_qty <> 0)
               OR EXISTS (SELECT 1 FROM mm_goods_movement m WHERE m.warehouse_id = w.id AND m.document_dt > @Today))
        """;

    // MM-BR-704: movements block a delete; so does any other row that refers to the warehouse, except a user default
    private const string SelectDeleteBlockers = """
        SELECT w.id,
               CASE WHEN EXISTS (SELECT 1 FROM mm_goods_movement m WHERE m.warehouse_id = w.id) THEN 'HAS_MOVEMENT' ELSE 'IN_USE' END AS reason
        FROM bc_warehouse w
        WHERE w.id = ANY(@Ids)
          AND (EXISTS (SELECT 1 FROM mm_goods_movement x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM mm_onhand x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM bc_location x WHERE x.warehouse_id = w.id AND NOT x.is_system)
               OR EXISTS (SELECT 1 FROM mm_monthly_inventory x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM mm_stock_count x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM mm_stock_transfer x WHERE x.from_warehouse_id = w.id OR x.to_warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM mm_goods_receipt x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM mm_goods_receipt_line x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM mm_unplanned_receipt x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM mm_unplanned_receipt_line x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM mm_purchase_order x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM mm_purchase_order_line x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM sd_sales_invoice x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM sd_sales_invoice_line x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM sd_shipment x WHERE x.warehouse_id = w.id)
               OR EXISTS (SELECT 1 FROM sd_shipment_line x WHERE x.warehouse_id = w.id))
        """;

    private const string ClearDefault = """
        UPDATE bc_warehouse SET is_default = false, updated_at = now(), updated_by = @UpdatedBy WHERE is_default
        """;

    // MM-BR-712: a new warehouse gets its system Location '*' in the same statement
    private const string Insert = """
        WITH w AS (
        INSERT INTO bc_warehouse (warehouse_code, warehouse_name, is_default, use_yn, warehouse_type, remarks, business_place_id,
                                  created_by, updated_by)
        SELECT @WarehouseCode, @WarehouseName, @IsDefault, @UseYn, @WarehouseType, @Remarks, p.id, @UpdatedBy, @UpdatedBy
        FROM bc_business_place p
        WHERE p.is_default
        RETURNING id
        ), location AS (
        INSERT INTO bc_location (warehouse_id, location_code, is_system, created_by, updated_by)
        SELECT id, '*', true, @UpdatedBy, @UpdatedBy FROM w
        )
        SELECT id FROM w
        """;

    private const string Update = """
        UPDATE bc_warehouse
        SET warehouse_name = @WarehouseName, is_default = @IsDefault, use_yn = @UseYn, remarks = @Remarks,
            updated_at = now(), updated_by = @UpdatedBy
        WHERE id = @Id
        """;

    private const string ClearUserDefaults = "UPDATE sys_app_user SET default_warehouse_id = NULL WHERE default_warehouse_id = ANY(@Ids)";

    // The system Location '*' goes with the warehouse; foreign keys are checked at the end of the statement
    private const string Delete = """
        WITH location AS (DELETE FROM bc_location WHERE warehouse_id = ANY(@Ids) AND is_system)
        DELETE FROM bc_warehouse WHERE id = ANY(@Ids)
        """;

    public async Task<IReadOnlyList<WarehouseOptionDto>> FindOptionsAsync(
        bool includeConsignment, bool includeInactive, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<WarehouseOptionDto>(new CommandDefinition(
            SelectOptions, new { IncludeConsignment = includeConsignment, IncludeInactive = includeInactive }, transaction,
            cancellationToken: cancellationToken))).ToList();

    public Task<ConsignmentWarehouse?> FindConsignmentAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<ConsignmentWarehouse?>(
            new CommandDefinition(SelectConsignment, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<WarehouseRow>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<StoredRow>(new CommandDefinition(
            forUpdate ? SelectAll + " FOR UPDATE" : SelectAll, transaction: transaction, cancellationToken: cancellationToken)))
        .Select(r => new WarehouseRow(
            r.Id, r.WarehouseCode, r.WarehouseName, r.IsDefault, r.UseYn, r.WarehouseType, r.Remarks,
            new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))))
        .ToList();

    public Task<IReadOnlyDictionary<long, string>> FindInactivationBlockersAsync(
        IReadOnlyCollection<long> warehouseIds, DateOnly today, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ReasonsAsync(SelectInactivationBlockers, new { Ids = warehouseIds.ToArray(), Today = today }, warehouseIds, transaction, cancellationToken);

    public Task<IReadOnlyDictionary<long, string>> FindDeleteBlockersAsync(
        IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ReasonsAsync(SelectDeleteBlockers, new { Ids = warehouseIds.ToArray() }, warehouseIds, transaction, cancellationToken);

    public Task ClearDefaultAsync(string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            ClearDefault, new { UpdatedBy = updatedBy }, transaction, cancellationToken: cancellationToken));

    public Task<long> InsertAsync(WarehouseState warehouse, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert, Parameters(warehouse, updatedBy), transaction, cancellationToken: cancellationToken));

    public Task UpdateAsync(WarehouseState warehouse, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update, Parameters(warehouse, updatedBy), transaction, cancellationToken: cancellationToken));

    public async Task DeleteAsync(IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (warehouseIds.Count == 0)
        {
            return;
        }
        var ids = new { Ids = warehouseIds.ToArray() };
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(ClearUserDefaults, ids, transaction, cancellationToken: cancellationToken));
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(Delete, ids, transaction, cancellationToken: cancellationToken));
    }

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record StoredRow(
        long Id, string WarehouseCode, string WarehouseName, bool IsDefault, bool UseYn, string WarehouseType, string? Remarks,
        DateTime UpdatedAt);

    private static object Parameters(WarehouseState w, string updatedBy) => new
    {
        w.Id,
        w.WarehouseCode,
        w.WarehouseName,
        w.IsDefault,
        w.UseYn,
        w.WarehouseType,
        w.Remarks,
        UpdatedBy = updatedBy,
    };

    private static async Task<IReadOnlyDictionary<long, string>> ReasonsAsync(
        string sql, object parameters, IReadOnlyCollection<long> warehouseIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (warehouseIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }
        var rows = await transaction.Connection!.QueryAsync<(long Id, string Reason)>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.Id, r => r.Reason);
    }
}

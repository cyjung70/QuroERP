namespace QuroErp.Contracts.Mm.Warehouses;

/// <summary>Warehouse API routes (MM-UC-017).</summary>
public static class WarehouseRoutes
{
    /// <summary>GET ?includeInactive= : every warehouse in code order, for any signed-in user.</summary>
    public const string Base = "api/mm/warehouses";

    /// <summary>POST the grid changes in one transaction (screens.md "유형별 저장 API 형태"). Needs Full Access on the screen.</summary>
    public const string Batch = Base + "/batch";

    /// <summary>
    /// GET: choice list of warehouses in use, for any signed-in user (TASK-CORE-022). The consignment warehouse is left out
    /// unless the query has includeConsignment=true, and warehouses not in use unless includeInactive=true; both are for report
    /// conditions (MM-BR-710, MM-UC-017).
    /// </summary>
    public const string Options = Base + "/options";
}

/// <summary>One choice in a warehouse select field.</summary>
/// <param name="WarehouseType">NORMAL or CONSIGNMENT (MM-BR-709).</param>
/// <param name="UseYn">false only with includeInactive=true; the screen adds "(not in use)" to its name.</param>
public sealed record WarehouseOptionDto(long Id, string WarehouseCode, string WarehouseName, string WarehouseType, bool UseYn);

/// <summary>A warehouse on the warehouse master screen (bc_warehouse).</summary>
/// <param name="IsDefault">The company default warehouse; exactly one (MM-BR-702).</param>
/// <param name="WarehouseType">NORMAL or CONSIGNMENT, read only (MM-BR-709).</param>
public sealed record WarehouseDto(
    long Id, string WarehouseCode, string WarehouseName, bool IsDefault, bool UseYn, string WarehouseType, string? Remarks,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A new or changed warehouse row. A new row has no Id; a changed row sends the Id and the updatedAt it read (409 when stale).
/// </summary>
/// <param name="WarehouseCode">Required for a new row; a changed row sends the stored code or none (MM-BR-701).</param>
/// <param name="IsDefault">true makes this the default warehouse and switches the previous one off (MM-BR-702). Null means false.</param>
/// <param name="UseYn">Null means in use.</param>
/// <param name="WarehouseType">Empty, or the stored type: users add NORMAL warehouses only and never change the type (MM-BR-709).</param>
public sealed record WarehouseSaveRow(
    long? Id, string? WarehouseCode, string? WarehouseName, bool? IsDefault, bool? UseYn, string? WarehouseType, string? Remarks,
    DateTimeOffset? UpdatedAt);

/// <summary>A deleted warehouse in a batch: its Id and the updatedAt it read.</summary>
public sealed record WarehouseKey(long Id, DateTimeOffset UpdatedAt);

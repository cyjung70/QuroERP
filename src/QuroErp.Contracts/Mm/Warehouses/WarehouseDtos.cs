namespace QuroErp.Contracts.Mm.Warehouses;

/// <summary>Warehouse API routes (MM warehouse master).</summary>
public static class WarehouseRoutes
{
    public const string Base = "api/mm/warehouses";

    /// <summary>GET: choice list of warehouses in use, for any signed-in user (TASK-CORE-022).</summary>
    public const string Options = Base + "/options";
}

/// <summary>One choice in a warehouse select field.</summary>
public sealed record WarehouseOptionDto(long Id, string WarehouseCode, string WarehouseName);

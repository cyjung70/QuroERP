namespace QuroErp.Contracts.Bc.SalesPrices;

/// <summary>Sales price API routes (BC-UC-014).</summary>
public static class SalesPriceRoutes
{
    /// <summary>GET ?priceLevel= (the price level's rows in item code order), POST batch?priceLevel= (grid changes in one transaction).</summary>
    public const string Base = "api/bc/sales-prices";

    public const string Batch = "batch";
}

/// <summary>A sales price row (bc_sales_price) with the item's standard sales price.</summary>
/// <param name="StdPrc">bc_item.so_usd_prc, read only (SD-BR-409).</param>
public sealed record SalesPriceDto(
    long Id, long ItemId, string ItemCd, string? ItemName, decimal SalesPrc, decimal? StdPrc, DateTimeOffset UpdatedAt);

/// <summary>An added or changed grid row of the price level in the query.</summary>
/// <param name="Id">Null for an added row.</param>
/// <param name="UpdatedAt">The value read, for a changed row (409 when someone saved first).</param>
public sealed record SalesPriceRowRequest(long? Id, string? ItemCd, decimal SalesPrc, DateTimeOffset? UpdatedAt);

/// <summary>A deleted grid row.</summary>
public sealed record SalesPriceDeleteRequest(long Id, DateTimeOffset UpdatedAt);

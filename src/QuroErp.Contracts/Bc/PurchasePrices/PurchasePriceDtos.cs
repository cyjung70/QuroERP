namespace QuroErp.Contracts.Bc.PurchasePrices;

/// <summary>Purchase price API routes (BC-UC-015).</summary>
public static class PurchasePriceRoutes
{
    /// <summary>GET ?vendorPrice= (the price group's rows in item code order), POST batch?vendorPrice= (grid changes in one transaction).</summary>
    public const string Base = "api/bc/purchase-prices";

    public const string Batch = "batch";
}

/// <summary>A purchase price row (bc_purchase_price) with the item's standard purchase price.</summary>
/// <param name="StdPrc">bc_item.usd_prc, read only (MM-BR-905).</param>
public sealed record PurchasePriceDto(
    long Id, long ItemId, string ItemCd, string? ItemName, decimal PurchasePrc, decimal? StdPrc, DateTimeOffset UpdatedAt);

/// <summary>An added or changed grid row of the price group in the query.</summary>
/// <param name="Id">Null for an added row.</param>
/// <param name="UpdatedAt">The value read, for a changed row (409 when someone saved first).</param>
public sealed record PurchasePriceRowRequest(long? Id, string? ItemCd, decimal PurchasePrc, DateTimeOffset? UpdatedAt);

/// <summary>A deleted grid row.</summary>
public sealed record PurchasePriceDeleteRequest(long Id, DateTimeOffset UpdatedAt);

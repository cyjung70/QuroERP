namespace QuroErp.Contracts.Mm.Locations;

/// <summary>Location (shelf) API routes (MM-UC-034).</summary>
public static class LocationRoutes
{
    /// <summary>GET ?warehouseId=&amp;code=&amp;includeInactive= : the Locations of one warehouse, '*' first, for any signed-in user.</summary>
    public const string Base = "api/mm/locations";

    /// <summary>POST the grid changes in one transaction (screens.md "유형별 저장 API 형태"). Needs Full Access on the screen.</summary>
    public const string Batch = Base + "/batch";

    /// <summary>GET ?warehouseId= : choice list of Locations in use, '*' first, for any signed-in user (MM-BR-717).</summary>
    public const string Options = Base + "/options";
}

/// <summary>Search conditions of the Location master. WarehouseId is required.</summary>
public sealed class LocationSearchQuery
{
    public long? WarehouseId { get; init; }

    /// <summary>Codes that start with this text, ignoring case.</summary>
    public string? Code { get; init; }

    /// <summary>true adds Locations not in use.</summary>
    public bool IncludeInactive { get; init; }
}

/// <summary>A Location on the master screen (bc_location).</summary>
/// <param name="IsSystem">The system Location '*' of the warehouse: read only (MM-BR-712).</param>
/// <param name="OnhandQty">The current balance of every item on the Location together.</param>
public sealed record LocationDto(
    long Id, long WarehouseId, string LocationCode, string? LocationName, bool IsSystem, bool UseYn, string? Remarks, decimal OnhandQty,
    DateTimeOffset UpdatedAt);

/// <summary>One choice in a Location select field.</summary>
public sealed record LocationOptionDto(long Id, string LocationCode, string? LocationName);

/// <summary>
/// A new or changed Location row. A new row sends WarehouseId and LocationCode and no Id; a changed row sends the Id and the
/// updatedAt it read (409 when stale).
/// </summary>
/// <param name="LocationCode">Required for a new row; a changed row sends the stored code or none (MM-BR-712).</param>
/// <param name="UseYn">Null means in use.</param>
public sealed record LocationSaveRow(
    long? Id, long? WarehouseId, string? LocationCode, string? LocationName, bool? UseYn, string? Remarks, DateTimeOffset? UpdatedAt);

/// <summary>A deleted Location in a batch: its Id and the updatedAt it read.</summary>
public sealed record LocationKey(long Id, DateTimeOffset UpdatedAt);

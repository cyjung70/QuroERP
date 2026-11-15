namespace QuroErp.Contracts.Mm.Pallets;

/// <summary>Pallet API routes (MM-UC-035).</summary>
public static class PalletRoutes
{
    /// <summary>GET ?palletNo=&amp;itemId=&amp;lotNo=&amp;warehouseId=&amp;locationId=&amp;status= : Pallets in number order, for any signed-in user.</summary>
    public const string Base = "api/mm/pallets";

    /// <summary>GET one Pallet with its history (MM-BR-916).</summary>
    public const string ById = Base + "/{id:long}";

    /// <summary>GET the Pallet of a scanned number (PDA). 404 when there is none.</summary>
    public const string ByNo = Base + "/by-no/{palletNo}";

    /// <summary>POST { qty, updatedAt }: splits a part off into a new Pallet (MM-BR-914). Needs Full Access on the screen.</summary>
    public const string Split = Base + "/{id:long}/split";

    /// <summary>POST { targetPalletId, sourcePalletIds }: merges Pallets into the target (MM-BR-915). Needs Full Access on the screen.</summary>
    public const string Merge = Base + "/merge";

    /// <summary>The most rows a search returns.</summary>
    public const int MaxListSize = 1000;
}

/// <summary>Pallet search conditions. Every condition is optional.</summary>
public sealed class PalletSearchQuery
{
    /// <summary>Pallet numbers that start with this text, ignoring case (a scanned number finds its Pallet).</summary>
    public string? PalletNo { get; init; }

    public long? ItemId { get; init; }

    /// <summary>The Lot number, ignoring case.</summary>
    public string? LotNo { get; init; }

    public long? WarehouseId { get; init; }

    public long? LocationId { get; init; }

    /// <summary>ACTIVE or CLOSED; none lists both.</summary>
    public string? Status { get; init; }
}

/// <summary>
/// A Pallet (mm_pallet): one item and one Lot in one warehouse and Location (MM-BR-911). ParentPalletNo is the Pallet it was split
/// from (MM-BR-914). Events is the history, only on a single Pallet read (MM-BR-916).
/// </summary>
public sealed record PalletDto(
    long Id, string PalletNo, long ItemId, string ItemCd, string? ItemName, long LotId, string LotNo, long WarehouseId, string WarehouseCode,
    long LocationId, string LocationCode, decimal Qty, string Status, string? ParentPalletNo, DateTimeOffset UpdatedAt,
    IReadOnlyList<PalletEventDto>? Events = null);

/// <summary>
/// One history row of a Pallet (mm_pallet_event): CREATE, MOVE, SHIP, SPLIT, MERGE or ADJUST with its signed qty, the other Pallet
/// of a split or merge, and the document it came from.
/// </summary>
public sealed record PalletEventDto(string EventType, DateOnly EventDt, decimal Qty, string? RelatedPalletNo, string? RefKey);

/// <summary>Split request: the qty of the new Pallet and the updatedAt of the Pallet as read (409 when stale).</summary>
public sealed record SplitPalletRequest(decimal Qty, DateTimeOffset UpdatedAt);

/// <summary>The new Pallet of a split.</summary>
public sealed record SplitPalletResultDto(long PalletId, string PalletNo);

/// <summary>Merge request: the Pallet that keeps the qty and the Pallets that are emptied into it.</summary>
public sealed record MergePalletsRequest(long TargetPalletId, IReadOnlyList<long>? SourcePalletIds);

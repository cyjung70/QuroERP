namespace QuroErp.Contracts.Bc.Items;

/// <summary>Alternate item API routes (BC-UC-012).</summary>
public static class AlternateItemRoutes
{
    public const string Base = "api/bc/alternate-items";
    public const string Batch = "batch";
}

/// <summary>The alternate item group to show, by its code.</summary>
public sealed record AlternateItemQuery
{
    public string? GroupCode { get; init; }
}

public sealed record AlternateItemDto(long Id, string ItemCd, string? ItemName);

/// <summary>A group with its items in item code order; UpdatedAt is sent back on save.</summary>
public sealed record AlternateItemGroupDto(
    long Id, string AltItemGroupCd, string AltItemGroupName, IReadOnlyList<AlternateItemDto> Items, DateTimeOffset UpdatedAt);

/// <summary>A changed row: <paramref name="ItemId"/> was on the row, <paramref name="ItemCd"/> is on it now.</summary>
public sealed record UpdatedAlternateItemRow(long ItemId, string? ItemCd);

/// <summary>
/// One grid save (MM-BR-811, MM-BR-813). Added rows bring items in by code, deleted rows take items out by id. MoveConfirmed is true
/// after the user agreed to move items that are in another group (MM-BR-812).
/// </summary>
public sealed record SaveAlternateItemsRequest(
    long GroupId,
    IReadOnlyList<string?>? Added,
    IReadOnlyList<UpdatedAlternateItemRow>? Updated,
    IReadOnlyList<long>? Deleted,
    bool MoveConfirmed,
    DateTimeOffset? UpdatedAt);

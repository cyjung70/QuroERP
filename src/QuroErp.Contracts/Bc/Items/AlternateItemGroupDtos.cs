namespace QuroErp.Contracts.Bc.Items;

/// <summary>Alternate item group API routes (BC-UC-019).</summary>
public static class AlternateItemGroupRoutes
{
    public const string Base = "api/bc/alternate-item-groups";
    public const string Batch = "batch";

    /// <summary>Most rows one search returns.</summary>
    public const int MaxListSize = 200;
}

/// <summary>
/// Groups of an item class (the group screen), or whose code or name contains the keyword (the group lookup). Both are optional.
/// </summary>
public sealed record AlternateItemGroupSearchQuery
{
    public string? ItemClass { get; init; }

    public string? Keyword { get; init; }
}

/// <param name="ItemCount">Items in the group; a group with items cannot be deleted (MM-BR-814).</param>
public sealed record AlternateItemGroupRowDto(
    long Id, string AltItemGroupCd, string AltItemGroupName, string ItemClass, long ItemCount, DateTimeOffset UpdatedAt);

public sealed record AlternateItemGroupInsert(string? AltItemGroupCd, string? AltItemGroupName);

public sealed record AlternateItemGroupUpdate(long Id, string? AltItemGroupCd, string? AltItemGroupName, DateTimeOffset UpdatedAt);

public sealed record AlternateItemGroupDelete(long Id, DateTimeOffset UpdatedAt);

/// <summary>One grid save of an item class (MM-BR-810, MM-BR-813). Added rows get <paramref name="ItemClass"/>; changed rows keep theirs.</summary>
public sealed record SaveAlternateItemGroupsRequest(
    string? ItemClass,
    IReadOnlyList<AlternateItemGroupInsert>? Added,
    IReadOnlyList<AlternateItemGroupUpdate>? Updated,
    IReadOnlyList<AlternateItemGroupDelete>? Deleted);

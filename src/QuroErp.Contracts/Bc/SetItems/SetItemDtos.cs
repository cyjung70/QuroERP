namespace QuroErp.Contracts.Bc.SetItems;

/// <summary>Set item composition API routes (BC-UC-013).</summary>
public static class SetItemRoutes
{
    /// <summary>GET ?itemCd= (the set and its components in sequence order), POST batch?itemCd= (grid changes in one transaction).</summary>
    public const string Base = "api/bc/set-items";

    public const string Batch = "batch";
}

/// <summary>A set item and its components. Copying another set (SD-BR-508) reads it here and adds its rows as new rows.</summary>
public sealed record SetItemDto(long ItemId, string ItemCd, string? ItemName, IReadOnlyList<SetComponentDto> Components);

/// <summary>A component row (sd_bom). Seq is set by the server and is the display order (SD-BR-505).</summary>
public sealed record SetComponentDto(
    long Id, int Seq, long ItemId, string ItemCd, string? ItemName, string? Unit, decimal Qty, DateTimeOffset UpdatedAt);

/// <summary>An added or changed component row of the set in the query. A change may replace the component and the qty.</summary>
/// <param name="Id">Null for an added row.</param>
/// <param name="UpdatedAt">The value read, for a changed row (409 when someone saved first).</param>
public sealed record SetComponentRowRequest(long? Id, string? ItemCd, decimal Qty, DateTimeOffset? UpdatedAt);

/// <summary>A deleted component row.</summary>
public sealed record SetComponentDeleteRequest(long Id, DateTimeOffset UpdatedAt);

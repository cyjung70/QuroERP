namespace QuroErp.Contracts.Bc.Items;

/// <summary>Item master API routes (BC-UC-009).</summary>
public static class ItemRoutes
{
    /// <summary>GET list (query <see cref="ItemSearchQuery"/>), POST new item.</summary>
    public const string Base = "api/bc/items";

    /// <summary>Default and largest page size (api.md "기타").</summary>
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;
}

/// <summary>
/// One item. MvgCost, OnhandQty and StkVal are read-only: only stock movements change them (MM-BR-804).
/// ItemGroupCd is set by the server (MM-BR-808).
/// </summary>
public sealed record ItemDto(
    long Id,
    string ItemCd,
    string? ItemName,
    string? ItemName2,
    string? ItemDesc,
    string? ItemType,
    string? Unit,
    string? ItemClass,
    string? ValidFlag,
    decimal UsdPrc,
    decimal SoUsdPrc,
    string? UpcCode,
    string? Location,
    decimal ReorderPoint,
    string? Memo1,
    string? Memo2,
    string? Memo3,
    string? Memo4,
    string? Memo5,
    string? ItemGrade,
    bool Taxable,
    string VatKind,
    string ItemGroupCd,
    decimal MvgCost,
    decimal OnhandQty,
    decimal StkVal,
    DateTimeOffset UpdatedAt);

/// <summary>A page of items in item class, item code order (the screen's previous and next order).</summary>
public sealed record ItemListDto(IReadOnlyList<ItemDto> Items, int Total);

/// <summary>List conditions. All are optional.</summary>
public sealed class ItemSearchQuery
{
    /// <summary>Item code starts with this (case-insensitive).</summary>
    public string? ItemCd { get; init; }

    /// <summary>Item name or name 2 contains this (case-insensitive).</summary>
    public string? ItemName { get; init; }

    public string? ItemClass { get; init; }

    public string? ValidFlag { get; init; }

    /// <summary>1-based.</summary>
    public int Page { get; init; } = 1;

    public int Size { get; init; } = ItemRoutes.DefaultPageSize;
}

/// <summary>
/// Values a user enters. Code values are Minor codes: ItemType ITEM_TYPE, Unit UNIT, ItemClass ITEM_CLASS,
/// ValidFlag VALID_FLAG, ItemGrade GRADE. VatKind is TAXABLE or EXEMPT (MM-BR-831).
/// </summary>
public abstract record ItemFieldsRequest
{
    public string? ItemName { get; init; }
    public string? ItemName2 { get; init; }
    public string? ItemDesc { get; init; }
    public string? ItemType { get; init; }
    public string? Unit { get; init; }
    public string? ItemClass { get; init; }
    public string? ValidFlag { get; init; }
    public decimal? UsdPrc { get; init; }
    public decimal? SoUsdPrc { get; init; }
    public string? UpcCode { get; init; }
    public string? Location { get; init; }
    public decimal? ReorderPoint { get; init; }
    public string? Memo1 { get; init; }
    public string? Memo2 { get; init; }
    public string? Memo3 { get; init; }
    public string? Memo4 { get; init; }
    public string? Memo5 { get; init; }
    public string? ItemGrade { get; init; }
    public bool? Taxable { get; init; }
    public string? VatKind { get; init; }
}

/// <summary>
/// New item. Missing ItemType, Unit, ValidFlag and UsdPrc get INVENTORY, EA, Y and 0.01 (MM-BR-802, 803).
/// </summary>
public sealed record CreateItemRequest : ItemFieldsRequest
{
    /// <summary>Upper case, up to 18 characters, unique (MM-BR-801).</summary>
    public string? ItemCd { get; init; }
}

/// <summary>Changed item. The item code cannot change (MM-BR-801).</summary>
public sealed record UpdateItemRequest : ItemFieldsRequest
{
    /// <summary>UpdatedAt of the item as read; 409 Common-E-004 when it changed since.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

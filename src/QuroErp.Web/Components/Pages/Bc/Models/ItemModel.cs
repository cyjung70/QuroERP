using QuroErp.Contracts.Bc.Items;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>
/// Editable item on the item master screen (BC-UC-009). <see cref="ItemDto"/> is immutable, so the form binds to this.
/// </summary>
public sealed class ItemModel
{
    // New item defaults shown on the screen (MM-BR-802, 803); the API applies the same defaults
    public const string DefaultItemType = "INVENTORY";
    public const string DefaultUnit = "EA";
    public const string DefaultValidFlag = "Y";
    public const decimal DefaultUsdPrc = 0.01m;
    public const string VatTaxable = "TAXABLE";
    public const string VatExempt = "EXEMPT";

    public long Id { get; set; }
    public string? ItemCd { get; set; }
    public string? ItemName { get; set; }
    public string? ItemName2 { get; set; }
    public string? ItemDesc { get; set; }
    public string? ItemType { get; set; } = DefaultItemType;
    public string? Unit { get; set; } = DefaultUnit;
    public string? ItemClass { get; set; }
    public string? ValidFlag { get; set; } = DefaultValidFlag;
    public decimal? UsdPrc { get; set; } = DefaultUsdPrc;
    public decimal? SoUsdPrc { get; set; } = 0m;
    public string? UpcCode { get; set; }
    public string? Location { get; set; }
    public decimal? ReorderPoint { get; set; } = 0m;
    public string? Memo1 { get; set; }
    public string? Memo2 { get; set; }
    public string? Memo3 { get; set; }
    public string? Memo4 { get; set; }
    public string? Memo5 { get; set; }
    public string? ItemGrade { get; set; }
    public bool Taxable { get; set; } = true;
    public string? VatKind { get; set; } = VatTaxable;

    // Read only: set by the server (MM-BR-804, 808)
    public decimal? MvgCost { get; set; } = 0m;
    public decimal? OnhandQty { get; set; } = 0m;
    public decimal? StkVal { get; set; } = 0m;
    public DateTimeOffset UpdatedAt { get; set; }

    public static ItemModel From(ItemDto dto) => new()
    {
        Id = dto.Id,
        ItemCd = dto.ItemCd,
        ItemName = dto.ItemName,
        ItemName2 = dto.ItemName2,
        ItemDesc = dto.ItemDesc,
        ItemType = dto.ItemType,
        Unit = dto.Unit,
        ItemClass = dto.ItemClass,
        ValidFlag = dto.ValidFlag,
        UsdPrc = dto.UsdPrc,
        SoUsdPrc = dto.SoUsdPrc,
        UpcCode = dto.UpcCode,
        Location = dto.Location,
        ReorderPoint = dto.ReorderPoint,
        Memo1 = dto.Memo1,
        Memo2 = dto.Memo2,
        Memo3 = dto.Memo3,
        Memo4 = dto.Memo4,
        Memo5 = dto.Memo5,
        ItemGrade = dto.ItemGrade,
        Taxable = dto.Taxable,
        VatKind = dto.VatKind,
        MvgCost = dto.MvgCost,
        OnhandQty = dto.OnhandQty,
        StkVal = dto.StkVal,
        UpdatedAt = dto.UpdatedAt,
    };

    public CreateItemRequest ToCreateRequest() => new()
    {
        ItemCd = ItemCd,
        ItemName = ItemName,
        ItemName2 = ItemName2,
        ItemDesc = ItemDesc,
        ItemType = ItemType,
        Unit = Unit,
        ItemClass = ItemClass,
        ValidFlag = ValidFlag,
        UsdPrc = UsdPrc,
        SoUsdPrc = SoUsdPrc,
        UpcCode = UpcCode,
        Location = Location,
        ReorderPoint = ReorderPoint,
        Memo1 = Memo1,
        Memo2 = Memo2,
        Memo3 = Memo3,
        Memo4 = Memo4,
        Memo5 = Memo5,
        ItemGrade = ItemGrade,
        Taxable = Taxable,
        VatKind = VatKind,
    };

    public UpdateItemRequest ToUpdateRequest() => new()
    {
        ItemName = ItemName,
        ItemName2 = ItemName2,
        ItemDesc = ItemDesc,
        ItemType = ItemType,
        Unit = Unit,
        ItemClass = ItemClass,
        ValidFlag = ValidFlag,
        UsdPrc = UsdPrc,
        SoUsdPrc = SoUsdPrc,
        UpcCode = UpcCode,
        Location = Location,
        ReorderPoint = ReorderPoint,
        Memo1 = Memo1,
        Memo2 = Memo2,
        Memo3 = Memo3,
        Memo4 = Memo4,
        Memo5 = Memo5,
        ItemGrade = ItemGrade,
        Taxable = Taxable,
        VatKind = VatKind,
        UpdatedAt = UpdatedAt,
    };
}

using QuroErp.Contracts.Mm.Pallets;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// A grid row on the Pallet screen (MM-UC-035), read only. StatusName is the status in the user's language; the stock key
/// (item, Lot, warehouse, Location) tells which Pallets can be merged (MM-BR-915).
/// </summary>
public sealed class PalletRow
{
    /// <summary>A Pallet with stock (MM-BR-911).</summary>
    public const string Active = "ACTIVE";

    public long Id { get; set; }
    public string PalletNo { get; set; } = "";
    public long ItemId { get; set; }
    public string? ItemCd { get; set; }
    public string? ItemName { get; set; }
    public long LotId { get; set; }
    public string? LotNo { get; set; }
    public long WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public long LocationId { get; set; }
    public string? LocationCode { get; set; }
    public decimal Qty { get; set; }
    public string Status { get; set; } = Active;
    public string? StatusName { get; set; }
    public string? ParentPalletNo { get; set; }

    /// <summary>The row's updatedAt when it was read; a split sends it back (409 when stale).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsActive => Status == Active;

    /// <summary>MM-BR-915: another ACTIVE Pallet of the same item, Lot, warehouse and Location.</summary>
    public bool CanMergeInto(PalletRow target) =>
        target is not null && Id != target.Id && IsActive
        && (ItemId, LotId, WarehouseId, LocationId) == (target.ItemId, target.LotId, target.WarehouseId, target.LocationId);

    public static PalletRow From(PalletDto dto, string? statusName) =>
        new()
        {
            Id = dto.Id,
            PalletNo = dto.PalletNo,
            ItemId = dto.ItemId,
            ItemCd = dto.ItemCd,
            ItemName = dto.ItemName,
            LotId = dto.LotId,
            LotNo = dto.LotNo,
            WarehouseId = dto.WarehouseId,
            WarehouseCode = dto.WarehouseCode,
            LocationId = dto.LocationId,
            LocationCode = dto.LocationCode,
            Qty = dto.Qty,
            Status = dto.Status,
            StatusName = statusName,
            ParentPalletNo = dto.ParentPalletNo,
            UpdatedAt = dto.UpdatedAt,
        };
}

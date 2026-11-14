using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// A grid row on the Location screen (MM-UC-034). The code is entered only on a new row; the system Location '*' is locked and
/// the balance is shown only.
/// </summary>
public sealed class LocationRow
{
    public long Id { get; set; }
    public long WarehouseId { get; set; }
    public string? LocationCode { get; set; }
    public string? LocationName { get; set; }
    public bool IsSystem { get; set; }
    public bool UseYn { get; set; } = true;
    public decimal OnhandQty { get; set; }
    public string? Remarks { get; set; }

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public static LocationRow From(LocationDto dto) =>
        new()
        {
            Id = dto.Id,
            WarehouseId = dto.WarehouseId,
            LocationCode = dto.LocationCode,
            LocationName = dto.LocationName,
            IsSystem = dto.IsSystem,
            UseYn = dto.UseYn,
            OnhandQty = dto.OnhandQty,
            Remarks = dto.Remarks,
            UpdatedAt = dto.UpdatedAt,
        };

    /// <summary>Grid changes as one batch request. New rows are saved under the warehouse the rows were read for.</summary>
    public static BatchRequest<LocationSaveRow, LocationKey> ToRequest(BatchChanges<LocationRow> changes, long warehouseId) =>
        new(
            [.. changes.Added.Select(r => new LocationSaveRow(null, warehouseId, r.LocationCode, r.LocationName, r.UseYn, r.Remarks, null))],
            [.. changes.Updated.Select(r => new LocationSaveRow(r.Id, r.WarehouseId, r.LocationCode, r.LocationName, r.UseYn, r.Remarks, r.UpdatedAt))],
            [.. changes.Deleted.Select(r => new LocationKey(r.Id, r.UpdatedAt!.Value))]);
}

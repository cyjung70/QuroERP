using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>A grid row on the warehouse screen (MM-UC-017). The code is entered only on a new row; the type is shown only.</summary>
public sealed class WarehouseRow
{
    /// <summary>The grid field of the default warehouse check, for the one-default rule (MM-BR-702).</summary>
    public const string IsDefaultField = "isDefault";

    public long Id { get; set; }
    public string? WarehouseCode { get; set; }
    public string? WarehouseName { get; set; }
    public bool IsDefault { get; set; }
    public bool UseYn { get; set; } = true;
    public string? WarehouseType { get; set; }
    public string? Remarks { get; set; }

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public static WarehouseRow From(WarehouseDto dto) =>
        new()
        {
            Id = dto.Id,
            WarehouseCode = dto.WarehouseCode,
            WarehouseName = dto.WarehouseName,
            IsDefault = dto.IsDefault,
            UseYn = dto.UseYn,
            WarehouseType = dto.WarehouseType,
            Remarks = dto.Remarks,
            UpdatedAt = dto.UpdatedAt,
        };

    /// <summary>Grid changes as one batch request. The type is not sent for a new row (the API makes it NORMAL, MM-BR-709).</summary>
    public static BatchRequest<WarehouseSaveRow, WarehouseKey> ToRequest(BatchChanges<WarehouseRow> changes) =>
        new(
            [.. changes.Added.Select(r => new WarehouseSaveRow(null, r.WarehouseCode, r.WarehouseName, r.IsDefault, r.UseYn, null, r.Remarks, null))],
            [.. changes.Updated.Select(r => new WarehouseSaveRow(
                r.Id, r.WarehouseCode, r.WarehouseName, r.IsDefault, r.UseYn, r.WarehouseType, r.Remarks, r.UpdatedAt))],
            [.. changes.Deleted.Select(r => new WarehouseKey(r.Id, r.UpdatedAt!.Value))]);
}

using QuroErp.Contracts.Bc.SetItems;
using QuroErp.Contracts.Common;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>
/// A component row on the set item composition screen (BC-UC-013). The server numbers new rows (SD-BR-505), so a row has no
/// sequence to edit; rows are shown in sequence order as read.
/// </summary>
public sealed class SetComponentRow
{
    public long Id { get; set; }
    public string? ItemCd { get; set; }

    /// <summary>Filled from the item lookup when the code is typed or picked, and from the server after a search.</summary>
    public string? ItemName { get; set; }

    public decimal? Qty { get; set; }

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public static SetComponentRow From(SetComponentDto dto) =>
        new() { Id = dto.Id, ItemCd = dto.ItemCd, ItemName = dto.ItemName, Qty = dto.Qty, UpdatedAt = dto.UpdatedAt };

    /// <summary>
    /// A component of another set as a new row of this one (SD-BR-508). The save checks it like a typed row (SD-BR-501~506).
    /// </summary>
    public static SetComponentRow CopyOf(SetComponentDto dto) => new() { ItemCd = dto.ItemCd, ItemName = dto.ItemName, Qty = dto.Qty };

    /// <summary>
    /// Grid changes as one batch request. Loaded rows always have updatedAt. A blank quantity goes as 0, which the API rejects as
    /// SD-E-503 with the row's item code; the API normalizes and checks every value.
    /// </summary>
    public static BatchRequest<SetComponentRowRequest, SetComponentDeleteRequest> ToRequest(BatchChanges<SetComponentRow> changes) =>
        new(
            [.. changes.Added.Select(r => new SetComponentRowRequest(null, r.ItemCd, r.Qty ?? 0, null))],
            [.. changes.Updated.Select(r => new SetComponentRowRequest(r.Id, r.ItemCd, r.Qty ?? 0, r.UpdatedAt))],
            [.. changes.Deleted.Select(r => new SetComponentDeleteRequest(r.Id, r.UpdatedAt!.Value))]);
}

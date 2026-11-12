using QuroErp.Contracts.Bc.SalesPrices;
using QuroErp.Contracts.Common;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>A row of one price level on the sales price screen (BC-UC-014).</summary>
public sealed class SalesPriceRow
{
    public long Id { get; set; }
    public string? ItemCd { get; set; }

    /// <summary>Filled from the item lookup when the code is typed or picked, and from the server after a search.</summary>
    public string? ItemName { get; set; }

    public decimal? SalesPrc { get; set; }

    /// <summary>The item's standard sales price, read only (SD-BR-409).</summary>
    public decimal? StdPrc { get; set; }

    /// <summary>The row's updatedAt when it was read; null for a new row.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public static SalesPriceRow From(SalesPriceDto dto) => new()
    {
        Id = dto.Id,
        ItemCd = dto.ItemCd,
        ItemName = dto.ItemName,
        SalesPrc = dto.SalesPrc,
        StdPrc = dto.StdPrc,
        UpdatedAt = dto.UpdatedAt,
    };

    /// <summary>
    /// Grid changes as one batch request. Loaded rows always have updatedAt. A blank price goes as 0, which the API rejects as
    /// SD-E-407 with the row's item code; the API normalizes and checks every value.
    /// </summary>
    public static BatchRequest<SalesPriceRowRequest, SalesPriceDeleteRequest> ToRequest(BatchChanges<SalesPriceRow> changes) =>
        new(
            [.. changes.Added.Select(r => new SalesPriceRowRequest(null, r.ItemCd, r.SalesPrc ?? 0, null))],
            [.. changes.Updated.Select(r => new SalesPriceRowRequest(r.Id, r.ItemCd, r.SalesPrc ?? 0, r.UpdatedAt))],
            [.. changes.Deleted.Select(r => new SalesPriceDeleteRequest(r.Id, r.UpdatedAt!.Value))]);
}

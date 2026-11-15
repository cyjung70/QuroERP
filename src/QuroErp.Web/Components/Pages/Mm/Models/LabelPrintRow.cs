using QuroErp.Contracts.Mm.Labels;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// A grid row on the label print screen (MM-UC-037): a target, whether it is picked and how many copies. Nothing is saved; a print
/// sends the picked rows. Copies is a number cell, so it is a decimal.
/// </summary>
public sealed class LabelPrintRow
{
    public const int DefaultCopies = 1;

    public long Id { get; set; }
    public bool Selected { get; set; }
    public decimal? Copies { get; set; } = DefaultCopies;
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string? ItemCd { get; set; }
    public string? LotNo { get; set; }
    public decimal? Qty { get; set; }
    public DateOnly? ExpiryDt { get; set; }
    public string? WarehouseCode { get; set; }
    public string? LocationCode { get; set; }

    public static LabelPrintRow From(LabelTargetDto dto) =>
        new()
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            ItemCd = dto.ItemCd,
            LotNo = dto.LotNo,
            Qty = dto.Qty,
            ExpiryDt = dto.ExpiryDt,
            WarehouseCode = dto.WarehouseCode,
            LocationCode = dto.LocationCode,
        };

    /// <summary>The picked rows in grid order with their copies; the API refuses copies outside 1 to 999.</summary>
    public static LabelPrintRequest ToRequest(string labelType, IEnumerable<LabelPrintRow> rows, string? outputKind) =>
        new(labelType, [.. rows.Where(r => r.Selected).Select(r => new LabelPrintTarget(r.Id, CopiesOf(r.Copies)))], outputKind, null);

    // A whole number in range goes as it is; anything else becomes 0, which the API refuses with the copies field named
    private static int CopiesOf(decimal? copies) =>
        copies is { } c && c == decimal.Truncate(c) && c is >= 1 and <= int.MaxValue ? (int)c : 0;
}

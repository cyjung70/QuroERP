using System.Globalization;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Mm.Models;

/// <summary>
/// A grid row on the label format screen (MM-UC-036). The label type never changes; the fields are chosen in a dialog and kept
/// here in order, joined by commas. Dpi is a combo value, so it is text.
/// </summary>
public sealed class LabelFormatRow
{
    public const char FieldSeparator = ',';

    public string LabelType { get; set; } = "";
    public string? OutputKind { get; set; }
    public decimal? WidthMm { get; set; }
    public decimal? HeightMm { get; set; }
    public string? Dpi { get; set; }

    /// <summary>The printed fields in order, joined by <see cref="FieldSeparator"/>.</summary>
    public string Fields { get; set; } = "";

    /// <summary>Every field the label type can print, for the field dialog.</summary>
    public IReadOnlyList<string> AvailableFields { get; set; } = [];

    public bool UseYn { get; set; } = true;

    /// <summary>The row's updatedAt when it was read; a save sends it back (409 when stale).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public IReadOnlyList<string> FieldList =>
        [.. Fields.Split(FieldSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static string Join(IEnumerable<string> fields) => string.Join(FieldSeparator, fields);

    public static LabelFormatRow From(LabelFormatDto dto) =>
        new()
        {
            LabelType = dto.LabelType,
            OutputKind = dto.OutputKind,
            WidthMm = dto.WidthMm,
            HeightMm = dto.HeightMm,
            Dpi = dto.Dpi.ToString(CultureInfo.InvariantCulture),
            Fields = Join(dto.Fields),
            AvailableFields = dto.AvailableFields,
            UseYn = dto.UseYn,
            UpdatedAt = dto.UpdatedAt,
        };

    /// <summary>The changed rows as one batch request. The formats are never added or deleted (MM-BR-917).</summary>
    public static BatchRequest<LabelFormatSaveRow, LabelFormatKey> ToRequest(BatchChanges<LabelFormatRow> changes) =>
        new(
            [],
            [.. changes.Updated.Select(r => new LabelFormatSaveRow(
                r.LabelType, r.OutputKind, r.WidthMm, r.HeightMm,
                int.TryParse(r.Dpi, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dpi) ? dpi : null,
                r.FieldList, r.UseYn, r.UpdatedAt))],
            []);
}

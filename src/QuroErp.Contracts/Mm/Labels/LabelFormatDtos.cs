namespace QuroErp.Contracts.Mm.Labels;

/// <summary>Label format API routes (MM-UC-036).</summary>
public static class LabelFormatRoutes
{
    /// <summary>GET: the four label formats, for any signed-in user.</summary>
    public const string Base = "api/mm/label-formats";

    /// <summary>POST the changed rows in one transaction (screens.md "유형별 저장 API 형태"). Needs Full Access on the screen.</summary>
    public const string Batch = Base + "/batch";

    /// <summary>GET: a sample label of the type as a PDF in the stored format.</summary>
    public const string Preview = Base + "/{labelType}/preview.pdf";

    public static string PreviewOf(string labelType) => $"{Base}/{Uri.EscapeDataString(labelType)}/preview.pdf";
}

/// <summary>A label format (bc_label_format, MM-BR-917).</summary>
/// <param name="LabelType">PALLET, ITEM, LOT or LOCATION; never changes.</param>
/// <param name="OutputKind">ZPL or PDF.</param>
/// <param name="Dpi">203 or 300.</param>
/// <param name="Fields">The fields printed, in order.</param>
/// <param name="AvailableFields">Every field a label of the type can print, for the field picker.</param>
public sealed record LabelFormatDto(
    string LabelType, string OutputKind, decimal WidthMm, decimal HeightMm, int Dpi, IReadOnlyList<string> Fields,
    IReadOnlyList<string> AvailableFields, bool UseYn, DateTimeOffset UpdatedAt);

/// <summary>A changed label format with the updatedAt it read (409 when stale). A value left out cannot be saved (MM-E-917).</summary>
public sealed record LabelFormatSaveRow(
    string LabelType, string? OutputKind, decimal? WidthMm, decimal? HeightMm, int? Dpi, IReadOnlyList<string>? Fields, bool? UseYn,
    DateTimeOffset UpdatedAt);

/// <summary>The key of a label format row. The four formats are never added or deleted (MM-BR-917).</summary>
public sealed record LabelFormatKey(string LabelType, DateTimeOffset UpdatedAt);

namespace QuroErp.Domain.Mm.Labels;

/// <summary>Label types and output kinds of bc_label_format (MM-BR-917).</summary>
public static class LabelType
{
    public const string Pallet = "PALLET";
    public const string Item = "ITEM";
    public const string Lot = "LOT";
    public const string Location = "LOCATION";

    public const string Zpl = "ZPL";
    public const string Pdf = "PDF";
}

/// <summary>
/// A label format (bc_label_format, MM-BR-917): the output kind, the size in mm, the resolution in dpi and the fields printed,
/// in order.
/// </summary>
public sealed record LabelFormat(
    string LabelType, string OutputKind, decimal WidthMm, decimal HeightMm, int Dpi, IReadOnlyList<string> Fields);

/// <summary>
/// One thing to print: the label type, the id of the Pallet, item, Lot or Location (mm_label_print.ref_id), the field values by
/// field name and the DataMatrix content (<see cref="LabelCode"/>).
/// </summary>
public sealed record Label(string LabelType, long RefId, IReadOnlyDictionary<string, string?> Values, string Code);

/// <summary>One printed page: the label's field text, its DataMatrix, or both.</summary>
public sealed record LabelPage(Label Label, bool Text, bool Barcode);

/// <summary>Which pages a label prints, in order (MM-BR-918).</summary>
public static class LabelPages
{
    /// <summary>
    /// A Pallet prints its Pallet label first, then a separate DataMatrix label. Item, Lot and Location labels print one page
    /// with the text and the DataMatrix.
    /// </summary>
    public static IReadOnlyList<LabelPage> For(IEnumerable<Label> labels) =>
        [.. labels.SelectMany(l => l.LabelType == LabelType.Pallet
            ? new[] { new LabelPage(l, Text: true, Barcode: false), new LabelPage(l, Text: false, Barcode: true) }
            : [new LabelPage(l, Text: true, Barcode: true)])];
}

using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Labels;

/// <summary>Field names a label prints (bc_label_format.fields, MM-BR-917), the keys of <see cref="Label.Values"/>.</summary>
public static class LabelField
{
    public const string PalletNo = "palletNo";
    public const string ItemCd = "itemCd";
    public const string ItemName = "itemName";
    public const string LotNo = "lotNo";
    public const string Qty = "qty";
    public const string ExpiryDt = "expiryDt";
    public const string WarehouseCode = "warehouseCode";
    public const string LocationCode = "locationCode";
}

/// <summary>
/// Label format rules (MM-BR-917): one format per label type with the output kind, a size above 0, a dpi the printers have and
/// one or more of the type's fields, each once. The sample label shows a format before anything is printed with it.
/// </summary>
public static class LabelFormatRules
{
    /// <summary>A format that cannot be saved. Arg: the label type.</summary>
    public const string InvalidCode = "MM-E-917";

    /// <summary>The label types, in screen order.</summary>
    public static IReadOnlyList<string> Types { get; } = [LabelType.Pallet, LabelType.Item, LabelType.Lot, LabelType.Location];

    public static IReadOnlyList<string> OutputKinds { get; } = [LabelType.Zpl, LabelType.Pdf];

    /// <summary>Zebra printer resolutions.</summary>
    public static IReadOnlyList<int> Dpis { get; } = [203, 300];

    private static readonly Dictionary<string, string[]> TypeFields = new(StringComparer.Ordinal)
    {
        [LabelType.Pallet] =
            [LabelField.PalletNo, LabelField.ItemCd, LabelField.ItemName, LabelField.LotNo, LabelField.Qty, LabelField.ExpiryDt],
        [LabelType.Item] = [LabelField.ItemCd, LabelField.ItemName],
        [LabelType.Lot] = [LabelField.ItemCd, LabelField.ItemName, LabelField.LotNo, LabelField.ExpiryDt],
        [LabelType.Location] = [LabelField.WarehouseCode, LabelField.LocationCode],
    };

    public static bool IsType(string? labelType) => labelType is not null && TypeFields.ContainsKey(labelType);

    /// <summary>The fields a label of the type can print, in their usual order.</summary>
    /// <exception cref="ArgumentException">An unknown label type.</exception>
    public static IReadOnlyList<string> FieldsOf(string labelType) =>
        TypeFields.TryGetValue(labelType, out var fields) ? fields : throw new ArgumentException($"Unknown label type {labelType}.", nameof(labelType));

    /// <summary>The format with the output kind in upper case and the field names trimmed, when it can be saved.</summary>
    /// <exception cref="DomainException">
    /// MM-E-917 for an unknown label type or output kind, a width or height not above 0, a dpi other than 203 or 300, no fields,
    /// or a field the type does not have or that is named twice.
    /// </exception>
    public static LabelFormat Validate(LabelFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var outputKind = format.OutputKind?.Trim().ToUpperInvariant() ?? "";
        var fields = (format.Fields ?? []).Select(f => f?.Trim() ?? "").ToList();
        if (!IsType(format.LabelType)
            || !OutputKinds.Contains(outputKind)
            || format.WidthMm <= 0 || format.HeightMm <= 0
            || !Dpis.Contains(format.Dpi)
            || fields.Count == 0
            || fields.Any(f => !TypeFields[format.LabelType].Contains(f))
            || fields.Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            throw new DomainException(InvalidCode, format.LabelType ?? "");
        }
        return format with { OutputKind = outputKind, Fields = fields };
    }

    /// <summary>A sample label of the type with made-up values and its DataMatrix content (MM-BR-918), for the preview.</summary>
    /// <exception cref="ArgumentException">An unknown label type.</exception>
    public static Label Sample(string labelType)
    {
        const string itemCd = "ITEM-001", itemName = "Sample Item", lotNo = "261101-001";
        var expiry = new DateOnly(2027, 12, 31);
        return labelType switch
        {
            LabelType.Pallet => LabelValues.Pallet(0, "PL2611000001", itemCd, itemName, lotNo, 10m, expiry),
            LabelType.Item => LabelValues.Item(0, itemCd, itemName),
            LabelType.Lot => LabelValues.Lot(0, itemCd, itemName, lotNo, expiry),
            LabelType.Location => LabelValues.Location(0, "MAIN", "A-01-01"),
            _ => throw new ArgumentException($"Unknown label type {labelType}.", nameof(labelType)),
        };
    }
}

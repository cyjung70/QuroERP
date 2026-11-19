namespace QuroErp.Contracts.Mm.Labels;

/// <summary>Scan API route (MM-BR-918, MM-BR-923), used by the PDA screens.</summary>
public static class LabelScanRoutes
{
    /// <summary>GET ?value=&amp;accept=PL&amp;accept=LT : reads a scanned value, for any signed-in user.</summary>
    public const string Base = "api/mm/label-scans";
}

/// <summary>The label kinds of the own-format DataMatrix (MM-BR-918).</summary>
public static class LabelKinds
{
    public const string Pallet = "PL";
    public const string Item = "IT";
    public const string Lot = "LT";
    public const string Location = "LC";
}

/// <summary>A scanned value and the kinds the current step takes (none: every kind).</summary>
public sealed class LabelScanQuery
{
    public string? Value { get; init; }

    /// <summary>PL, IT, LT or LC.</summary>
    public IReadOnlyList<string>? Accept { get; init; }
}

/// <summary>
/// A scanned value read by MM-BR-918 and MM-BR-923. Kind is PL, IT, LT or LC and Parts are the values after it. A value without a
/// kind found as an item barcode or item code comes back as IT with the item code as its one part. The item is that of PL, IT and
/// LT; LC has none.
/// </summary>
public sealed record LabelScanDto(string Kind, IReadOnlyList<string> Parts, long? ItemId, string? ItemCd, string? ItemName);

using System.Globalization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Labels;

/// <summary>A scanned value: its kind (PL, IT, LT, LC, or <see cref="LabelCode.Plain"/>) and the values after the kind.</summary>
public sealed record ScanValue(string Kind, IReadOnlyList<string> Parts);

/// <summary>
/// Content of the own-format DataMatrix (MM-BR-918): <c>kind|value…</c> with <c>|</c> between the values. Not GS1 or SSCC. PDA
/// and screen scans read it with <see cref="Parse"/> (MM-BR-923).
/// </summary>
public static class LabelCode
{
    public const string Pallet = "PL";
    public const string Item = "IT";
    public const string Lot = "LT";
    public const string Location = "LC";

    /// <summary>A value without a kind: the caller looks it up as an item barcode (upc_code), then an item code (MM-BR-923).</summary>
    public const string Plain = "";

    /// <summary>A scanned value is not a label of this system. Args: the scanned value.</summary>
    public const string UnknownCode = "MM-E-923";

    public const char Separator = '|';

    // Number of values after each kind
    private static readonly Dictionary<string, int> PartCounts = new(StringComparer.Ordinal)
    {
        [Pallet] = 5,
        [Item] = 1,
        [Lot] = 2,
        [Location] = 2,
    };

    /// <summary><c>PL|Pallet number|item code|Lot number|qty|expiry (yyyymmdd)</c>; the expiry is empty when the Lot has none.</summary>
    public static string ForPallet(string palletNo, string itemCd, string lotNo, decimal qty, DateOnly? expiryDt) =>
        Join(Pallet, palletNo, itemCd, lotNo, qty.ToString("0.############", CultureInfo.InvariantCulture),
            expiryDt?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "");

    /// <summary><c>IT|item code</c>.</summary>
    public static string ForItem(string itemCd) => Join(Item, itemCd);

    /// <summary><c>LT|item code|Lot number</c>.</summary>
    public static string ForLot(string itemCd, string lotNo) => Join(Lot, itemCd, lotNo);

    /// <summary><c>LC|warehouse code|Location code</c>.</summary>
    public static string ForLocation(string warehouseCode, string locationCode) => Join(Location, warehouseCode, locationCode);

    /// <summary>
    /// Reads a scanned value. A value without <c>|</c> is <see cref="Plain"/> with the value as its one part. A value with a kind
    /// must have the kind's number of values, all filled except the Pallet expiry.
    /// </summary>
    /// <exception cref="DomainException">MM-E-923 for an empty value, an unknown kind or the wrong number of values.</exception>
    public static ScanValue Parse(string? scanned)
    {
        var value = scanned?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            throw new DomainException(UnknownCode, scanned ?? "");
        }
        if (!value.Contains(Separator, StringComparison.Ordinal))
        {
            return new ScanValue(Plain, [value]);
        }
        var parts = value.Split(Separator);
        var kind = parts[0];
        if (!PartCounts.TryGetValue(kind, out var count) || parts.Length != count + 1)
        {
            throw new DomainException(UnknownCode, value);
        }
        var values = parts[1..];
        // Every value is required except the Pallet expiry (the last value)
        var required = kind == Pallet ? values[..^1] : values;
        if (required.Any(string.IsNullOrEmpty)
            || (kind == Pallet && !decimal.TryParse(values[3], NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            || (kind == Pallet && values[4].Length > 0
                && !DateOnly.TryParseExact(values[4], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
        {
            throw new DomainException(UnknownCode, value);
        }
        return new ScanValue(kind, values);
    }

    private static string Join(string kind, params string[] values)
    {
        foreach (var value in values)
        {
            if (value.Contains(Separator, StringComparison.Ordinal))
            {
                throw new ArgumentException($"A label value cannot contain '{Separator}': {value}", nameof(values));
            }
        }
        return string.Join(Separator, [kind, .. values]);
    }
}

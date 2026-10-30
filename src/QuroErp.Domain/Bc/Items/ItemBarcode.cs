using System.Globalization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Items;

/// <summary>Item barcode (bc_item.upc_code) generation rules MM-BR-815~817.</summary>
public static class ItemBarcode
{
    /// <summary>Start number not digits, or a generated barcode longer than MaxDigits. Arg: MaxDigits.</summary>
    public const string InvalidStartNoCode = "MM-E-816";

    /// <summary>A barcode another item already has. Arg: the other item codes.</summary>
    public const string DuplicateCode = "MM-E-817";

    /// <summary>Generated barcodes only; a typed barcode may be up to 20 characters (MM-BR-825).</summary>
    public const int MaxDigits = 10;

    public const string DefaultStartNo = "1000000000";

    private const long MaxValue = 9_999_999_999;

    /// <summary>MM-BR-815, MM-BR-816: start number + 1, + 2, … for count items, in the order the items are given.</summary>
    /// <exception cref="DomainException">MM-E-816.</exception>
    public static IReadOnlyList<string> Generate(string? startNo, int count)
    {
        var start = startNo?.Trim();
        if (string.IsNullOrEmpty(start) || start.Length > MaxDigits || !start.All(char.IsAsciiDigit)
            || long.Parse(start, CultureInfo.InvariantCulture) + count > MaxValue)
        {
            throw new DomainException(InvalidStartNoCode, MaxDigits);
        }
        var first = long.Parse(start, CultureInfo.InvariantCulture);
        return Enumerable.Range(1, count).Select(i => (first + i).ToString(CultureInfo.InvariantCulture)).ToList();
    }

    /// <summary>MM-BR-817: the other item codes, for the message argument.</summary>
    public static DomainException Duplicate(IEnumerable<string> otherItemCds) =>
        new(DuplicateCode, string.Join(", ", otherItemCds.Distinct().Order(StringComparer.Ordinal)));
}

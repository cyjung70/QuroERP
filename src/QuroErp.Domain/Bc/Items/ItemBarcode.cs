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

    /// <summary>A generated barcode, like a typed one (bc_item.upc_code, MM-BR-816, MM-BR-825).</summary>
    public const int MaxDigits = 20;

    public const string DefaultStartNo = "1000000000";

    // 20 digits do not fit in a long, so the numbers are decimals (28 digits)
    private const decimal MaxValue = 99_999_999_999_999_999_999m;

    /// <summary>MM-BR-815, MM-BR-816: start number + 1, + 2, … for count items, in the order the items are given.</summary>
    /// <exception cref="DomainException">MM-E-816.</exception>
    public static IReadOnlyList<string> Generate(string? startNo, int count)
    {
        var start = startNo?.Trim();
        if (string.IsNullOrEmpty(start) || start.Length > MaxDigits || !start.All(char.IsAsciiDigit)
            || decimal.Parse(start, CultureInfo.InvariantCulture) + count > MaxValue)
        {
            throw new DomainException(InvalidStartNoCode, MaxDigits);
        }
        var first = decimal.Parse(start, CultureInfo.InvariantCulture);
        return Enumerable.Range(1, count).Select(i => (first + i).ToString(CultureInfo.InvariantCulture)).ToList();
    }

    /// <summary>MM-BR-817: the other item codes, for the message argument.</summary>
    public static DomainException Duplicate(IEnumerable<string> otherItemCds) =>
        new(DuplicateCode, string.Join(", ", otherItemCds.Distinct().Order(StringComparer.Ordinal)));
}

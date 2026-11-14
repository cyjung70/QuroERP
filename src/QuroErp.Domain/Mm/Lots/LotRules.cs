using System.Globalization;
using System.Text.RegularExpressions;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Lots;

/// <summary>
/// Lot rules MM-BR-351~356 and 361 (MM lot.md). Stock is kept per item + warehouse + Location + Lot; an item without lot
/// management uses Lot '*' only (MM-BR-363). The lot balance check is <see cref="Inventory.StockKeyRules.EnsureBalance"/>.
/// </summary>
public static partial class LotRules
{
    /// <summary>
    /// Lot management cannot be turned on or off: not an Inventory item, or stock left on a lot other than '*'. Arg: item code.
    /// </summary>
    public const string ManagementCode = "MM-E-351";

    /// <summary>Lot number missing the rules ('*' is the system lot and cannot be typed). Args: item code, lot number.</summary>
    public const string InvalidNumberCode = "MM-E-352";

    /// <summary>A line that decreases a lot-managed item's stock has no lot of that item. Arg: item code.</summary>
    public const string RequiredCode = "MM-E-355";

    /// <summary>
    /// A lot balance would go below zero, or the movement is dated before lot management started. Args: item code,
    /// warehouse code, Location code, lot number, date (yyyy-MM-dd), lot balance.
    /// </summary>
    public const string NegativeCode = "MM-E-356";

    public const int NumberMaxLength = 30;

    /// <summary>
    /// A lot number a user typed: trimmed and upper case. Empty means "number it for me" and returns null (MM-BR-353).
    /// </summary>
    /// <exception cref="DomainException">MM-E-352 for other characters (the system lot '*' among them) or more than 30 characters.</exception>
    public static string? NormalizeNumber(string itemCd, string? lotNo)
    {
        var value = lotNo?.Trim().ToUpperInvariant() ?? "";
        if (value.Length == 0)
        {
            return null;
        }
        if (value.Length > NumberMaxLength || !NumberPattern().IsMatch(value))
        {
            throw new DomainException(InvalidNumberCode, itemCd, value);
        }
        return value;
    }

    /// <summary>The prefix of automatic lot numbers of a document date: yymmdd- (MM-BR-353).</summary>
    public static string AutoNumberPrefix(DateOnly documentDt) =>
        documentDt.ToString("yyMMdd", CultureInfo.InvariantCulture) + "-";

    /// <summary>
    /// The next automatic lot number: yymmdd-nnn, the item's next number of that date. Past 999 it keeps counting with more digits.
    /// </summary>
    /// <param name="lastSeq">The largest number already used with this prefix for the item, 0 when none.</param>
    public static string AutoNumber(DateOnly documentDt, int lastSeq) =>
        AutoNumberPrefix(documentDt) + (lastSeq + 1).ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>
    /// A movement of a lot other than '*' may not be dated before the day lot management started (MM-BR-356).
    /// </summary>
    /// <exception cref="DomainException">MM-E-356 with the current lot balance.</exception>
    public static void EnsureManagedFrom(
        DateOnly documentDt, DateOnly? lotManagedFrom, string itemCd, string warehouseCode, string locationCode, string lotNo,
        decimal lotBalance)
    {
        if (lotNo != Inventory.StockKeyRules.System && lotManagedFrom is { } from && documentDt < from)
        {
            throw new DomainException(NegativeCode, itemCd, warehouseCode, locationCode, lotNo, Day(documentDt), lotBalance);
        }
    }

    /// <summary>
    /// Lot management can be turned on for an item with its own stock (Inventory or Set) only (MM-BR-351). The stock stays on
    /// Lot '*', so a warehouse below zero does not matter. A Kit or Set component can be lot-managed (MM-BR-361).
    /// </summary>
    /// <exception cref="DomainException">MM-E-351.</exception>
    public static void EnsureCanTurnOn(string itemCd, bool ownStockItem)
    {
        if (!ownStockItem)
        {
            throw new DomainException(ManagementCode, itemCd);
        }
    }

    /// <summary>Lot management can be turned off only when every balance on a lot other than '*' is 0 (MM-BR-351).</summary>
    /// <exception cref="DomainException">MM-E-351.</exception>
    public static void EnsureCanTurnOff(string itemCd, bool lotStockLeft)
    {
        if (lotStockLeft)
        {
            throw new DomainException(ManagementCode, itemCd);
        }
    }

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[A-Z0-9_-]+$")]
    private static partial Regex NumberPattern();
}

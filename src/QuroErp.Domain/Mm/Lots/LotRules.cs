using System.Globalization;
using System.Text.RegularExpressions;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Lots;

/// <summary>Daily ledger sum of one item + warehouse + lot.</summary>
public sealed record LotDailyQty(DateOnly Date, decimal Qty);

/// <summary>
/// Lot rules MM-BR-351~356 and 361 (MM lot.md). A lot-managed item keeps stock per item + warehouse + lot; items without lot
/// management never get a lot.
/// </summary>
public static partial class LotRules
{
    /// <summary>
    /// Lot management cannot be turned on or off: not an Inventory item, a warehouse with negative stock, or lot stock left.
    /// Arg: item code.
    /// </summary>
    public const string ManagementCode = "MM-E-351";

    /// <summary>Lot number missing the rules, or a system lot number typed by a user. Args: item code, lot number.</summary>
    public const string InvalidNumberCode = "MM-E-352";

    /// <summary>A line that decreases a lot-managed item's stock has no lot of that item. Arg: item code.</summary>
    public const string RequiredCode = "MM-E-355";

    /// <summary>
    /// A lot balance would go below zero, or the movement is dated before lot management started. Args: item code,
    /// warehouse code, lot number, date (yyyy-MM-dd), lot balance.
    /// </summary>
    public const string NegativeCode = "MM-E-356";

    /// <summary>The stock an item had when lot management was turned on (MM-BR-351).</summary>
    public const string Migrated = "MIGRATED";

    /// <summary>Returned stock whose original lot is not known (MM-BR-359).</summary>
    public const string Unknown = "UNKNOWN";

    public const int NumberMaxLength = 30;

    /// <summary>
    /// A lot number a user typed: trimmed and upper case. Empty means "number it for me" and returns null (MM-BR-353).
    /// </summary>
    /// <exception cref="DomainException">MM-E-352 for other characters, more than 30 characters, or MIGRATED / UNKNOWN.</exception>
    public static string? NormalizeNumber(string itemCd, string? lotNo)
    {
        var value = lotNo?.Trim().ToUpperInvariant() ?? "";
        if (value.Length == 0)
        {
            return null;
        }
        if (value.Length > NumberMaxLength || !NumberPattern().IsMatch(value) || value is Migrated or Unknown)
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
    /// A lot-managed item's movements may not be dated before the day lot management started (MM-BR-356).
    /// </summary>
    /// <exception cref="DomainException">MM-E-356 with the current lot balance.</exception>
    public static void EnsureManagedFrom(
        DateOnly documentDt, DateOnly? lotManagedFrom, string itemCd, string warehouseCode, string lotNo, decimal lotBalance)
    {
        if (lotManagedFrom is { } from && documentDt < from)
        {
            throw new DomainException(NegativeCode, itemCd, warehouseCode, lotNo, Day(documentDt), lotBalance);
        }
    }

    /// <summary>
    /// A decrease of qty dated documentDt may not leave the lot balance below zero now or at the end of that day or any later day
    /// with movements, whatever the negative stock mode is (MM-BR-356). The balance at the end of day d is the current balance
    /// minus the ledger movements dated after d, so stock moved to MIGRATED without a movement counts from the start.
    /// </summary>
    /// <param name="current">The locked lot balance, including earlier lines of this save.</param>
    /// <param name="daily">Ledger sums of the lot on documentDt and later days, without this movement.</param>
    /// <exception cref="DomainException">MM-E-356 with the first day that would go negative and its balance.</exception>
    public static void EnsureBalance(
        DateOnly documentDt, decimal qty, decimal current, IReadOnlyList<LotDailyQty> daily, string itemCd, string warehouseCode, string lotNo)
    {
        if (qty >= 0)
        {
            return;
        }
        var days = daily.Where(d => d.Date > documentDt).Select(d => d.Date).Prepend(documentDt).Distinct().Order();
        foreach (var day in days)
        {
            var balance = current - daily.Where(d => d.Date > day).Sum(d => d.Qty) + qty;
            if (balance < 0)
            {
                throw new DomainException(NegativeCode, itemCd, warehouseCode, lotNo, Day(day), balance);
            }
        }
    }

    /// <summary>
    /// Lot management can be turned on for an Inventory item only, with no warehouse below zero (MM-BR-351). A Kit or Set
    /// component can be lot-managed (MM-BR-361).
    /// </summary>
    /// <exception cref="DomainException">MM-E-351.</exception>
    public static void EnsureCanTurnOn(string itemCd, bool inventoryItem, bool negativeWarehouse)
    {
        if (!inventoryItem || negativeWarehouse)
        {
            throw new DomainException(ManagementCode, itemCd);
        }
    }

    /// <summary>Lot management can be turned off only when every lot balance of the item is 0 (MM-BR-351).</summary>
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

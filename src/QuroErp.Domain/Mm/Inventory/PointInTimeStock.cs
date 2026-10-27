using System.Globalization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>Ledger balance of one item + warehouse at the end of a day with movements (MM-BR-604).</summary>
/// <param name="MonthEnd">The last day with movements in its month: its balance is the month-end balance.</param>
public sealed record DailyStockBalance(DateOnly Date, decimal Balance, bool MonthEnd);

/// <summary>
/// Point-in-time and month-end negative stock checks for a movement dated in the past or future
/// (MM-BR-009, MM-BR-010). Infrastructure reads the balances without the new movement; the decision is made here.
/// </summary>
public static class PointInTimeStock
{
    /// <summary>A balance on or after the movement date would go negative. Args: item, warehouse, date, balance.</summary>
    public const string NegativeOnDate = "MM-E-009";

    /// <summary>A month-end balance would go negative. Args: item, warehouse, month (YYYYMM), month-end balance.</summary>
    public const string NegativeAtMonthEnd = "MM-E-010";

    /// <param name="opening">Closing balance of the last closed month, 0 when no month is closed.</param>
    /// <param name="balances">
    /// Daily balances in date order, starting with the last day with movements on or before movementDate (if any)
    /// and then every later day with movements.
    /// </param>
    /// <returns>
    /// true when a balance on or after movementDate goes below zero but the movement is allowed: ALLOW_IN_MONTH with
    /// only mid-month negatives (flag it, MM-BR-002).
    /// </returns>
    /// <exception cref="DomainException">MM-E-009 in DENY mode, MM-E-010 in ALLOW_IN_MONTH mode.</exception>
    public static bool Check(
        NegativeStockMode mode, DateOnly movementDate, decimal qty, decimal opening, IReadOnlyList<DailyStockBalance> balances,
        string itemCd, string warehouseCode)
    {
        // Only movements that decrease stock are checked (MM-BR-602)
        if (qty >= 0 || mode == NegativeStockMode.Allow)
        {
            return false;
        }

        var dayBalance = balances.Count > 0 && balances[0].Date <= movementDate ? balances[0].Balance : opening;
        var later = balances.Where(b => b.Date > movementDate).ToList();

        if (mode == NegativeStockMode.Deny)
        {
            foreach (var (date, balance) in later.Select(b => (b.Date, b.Balance)).Prepend((movementDate, dayBalance)))
            {
                if (balance + qty < 0)
                {
                    throw new DomainException(NegativeOnDate, itemCd, warehouseCode, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), balance + qty);
                }
            }
            return false;
        }

        // ALLOW_IN_MONTH: the movement month ends on its last later day, or on the movement date when there is none
        var monthEnds = new List<(DateOnly Date, decimal Balance)>
        {
            later.LastOrDefault(b => SameMonth(b.Date, movementDate)) is { } last ? (last.Date, last.Balance) : (movementDate, dayBalance),
        };
        monthEnds.AddRange(later.Where(b => b.MonthEnd && !SameMonth(b.Date, movementDate)).Select(b => (b.Date, b.Balance)));
        foreach (var (date, balance) in monthEnds)
        {
            if (balance + qty < 0)
            {
                throw new DomainException(NegativeAtMonthEnd, itemCd, warehouseCode, date.ToString("yyyyMM", CultureInfo.InvariantCulture), balance + qty);
            }
        }
        return dayBalance + qty < 0 || later.Any(b => b.Balance + qty < 0);
    }

    private static bool SameMonth(DateOnly a, DateOnly b) => a.Year == b.Year && a.Month == b.Month;
}

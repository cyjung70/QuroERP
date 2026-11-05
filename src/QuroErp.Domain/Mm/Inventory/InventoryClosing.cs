using System.Globalization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>
/// Closed months (MM closing.md). bc_company.close_year_month (YYYYMM) is the first month not closed yet;
/// null means no month is closed.
/// </summary>
public static class InventoryClosing
{
    /// <summary>The movement date is in a closed month. Args: movement date, last closed month (YYYYMM).</summary>
    public const string ClosedMonth = "MM-E-002";

    /// <returns>The last day of the last closed month, or null when no month is closed.</returns>
    public static DateOnly? LastClosedMonthEnd(string? closeYearMonth)
    {
        if (closeYearMonth is null)
        {
            return null;
        }
        if (!DateOnly.TryParseExact(closeYearMonth + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstOpenDay))
        {
            throw new InvalidOperationException($"bc_company.close_year_month '{closeYearMonth}' is not YYYYMM.");
        }
        return firstOpenDay.AddDays(-1);
    }

    /// <summary>
    /// The date a price difference (PV) is posted on: the invoice date, or the first day of the first open month when the
    /// invoice date is in a closed month (MM-BR-759).
    /// </summary>
    public static DateOnly OpenDateFor(string? closeYearMonth, DateOnly date) =>
        LastClosedMonthEnd(closeYearMonth) is { } end && date <= end ? end.AddDays(1) : date;

    /// <summary>Movements dated in a closed month cannot be added (MM-BR-004). A reversal uses its own date.</summary>
    /// <exception cref="DomainException">MM-E-002 when documentDt is on or before the last closed month end.</exception>
    public static void EnsureOpen(string? closeYearMonth, DateOnly documentDt)
    {
        var closedEnd = LastClosedMonthEnd(closeYearMonth);
        if (closedEnd is { } end && documentDt <= end)
        {
            throw new DomainException(ClosedMonth, documentDt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end.ToString("yyyyMM", CultureInfo.InvariantCulture));
        }
    }
}

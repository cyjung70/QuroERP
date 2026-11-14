using System.Globalization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>Daily ledger sum of one stock key (item + warehouse + Location + Lot).</summary>
public sealed record StockKeyDailyQty(DateOnly Date, decimal Qty);

/// <summary>A bc_location row as a stock line sees it.</summary>
public sealed record StockLocation(long Id, long WarehouseId, string LocationCode, bool InUse);

/// <summary>
/// Rules of the stock key item + warehouse + Location + Lot (MM-BR-713, MM-BR-714, MM-BR-718, MM-BR-363, MM-BR-356). Stock is
/// kept per key in mm_onhand; the item + warehouse balance is the sum of its keys.
/// </summary>
public static class StockKeyRules
{
    /// <summary>The system Location of every warehouse and the system Lot of every item (MM-BR-712, MM-BR-363).</summary>
    public const string System = "*";

    /// <summary>
    /// A line Location that is not a Location in use of the line warehouse, or not '*' for an item without Location management.
    /// Args: item code, warehouse code, Location code (or id when it does not exist).
    /// </summary>
    public const string LocationCode = "MM-E-713";

    /// <summary>A Lot that is not '*' for an item without lot management. Args: item code, lot number.</summary>
    public const string LotCode = "MM-E-363";

    /// <summary>
    /// A Location balance would go below zero now or on a later day. Args: item code, warehouse code, Location code, lot number,
    /// balance.
    /// </summary>
    public const string NegativeCode = "MM-E-714";

    /// <summary>Location management cannot be turned off while a Location other than '*' has a balance. Arg: item code.</summary>
    public const string LocationManagementCode = "MM-E-718";

    /// <summary>
    /// A line may use the Location: one of the line warehouse, in use, and '*' unless the item is Location managed (MM-BR-713,
    /// MM-BR-717). '*' is always in use.
    /// </summary>
    /// <param name="location">The asked Location, null when no Location has the asked id.</param>
    /// <param name="requested">What the caller asked for, for the message when the Location does not exist.</param>
    /// <exception cref="DomainException">MM-E-713.</exception>
    public static void EnsureLocation(
        string itemCd, bool locationManaged, long warehouseId, string warehouseCode, StockLocation? location, string requested)
    {
        if (location is null || location.WarehouseId != warehouseId)
        {
            throw new DomainException(LocationCode, itemCd, warehouseCode, location?.LocationCode ?? requested);
        }
        if (location.LocationCode != System && (!locationManaged || !location.InUse))
        {
            throw new DomainException(LocationCode, itemCd, warehouseCode, location.LocationCode);
        }
    }

    /// <summary>An item without lot management uses Lot '*' only (MM-BR-363).</summary>
    /// <exception cref="DomainException">MM-E-363.</exception>
    public static void EnsureLot(string itemCd, bool lotManaged, string lotNo)
    {
        if (lotNo != System && !lotManaged)
        {
            throw new DomainException(LotCode, itemCd, lotNo);
        }
    }

    /// <summary>
    /// A key whose Location or Lot is not '*' never goes below zero (MM-BR-714, MM-BR-356). The ('*', '*') key follows the negative
    /// stock mode: it stays at zero or above in DENY and may go below zero in ALLOW_IN_MONTH.
    /// </summary>
    public static bool MustStayNonNegative(NegativeStockMode mode, string locationCode, string lotNo) =>
        locationCode != System || lotNo != System || mode == NegativeStockMode.Deny;

    /// <summary>
    /// A decrease of qty dated documentDt may not leave the key balance below zero now or at the end of that day or any later
    /// day with movements. The balance at the end of day d is the current balance minus the ledger movements dated after d.
    /// </summary>
    /// <param name="current">The locked key balance, including earlier lines of this save.</param>
    /// <param name="daily">Ledger sums of the key on documentDt and later days, without this movement.</param>
    /// <exception cref="DomainException">
    /// MM-E-356 for a Lot other than '*' with the first day that would go negative and its balance, otherwise MM-E-714.
    /// </exception>
    public static void EnsureBalance(
        DateOnly documentDt, decimal qty, decimal current, IReadOnlyList<StockKeyDailyQty> daily,
        string itemCd, string warehouseCode, string locationCode, string lotNo)
    {
        if (qty >= 0)
        {
            return;
        }
        var days = daily.Where(d => d.Date > documentDt).Select(d => d.Date).Prepend(documentDt).Distinct().Order();
        foreach (var day in days)
        {
            var balance = current - daily.Where(d => d.Date > day).Sum(d => d.Qty) + qty;
            if (balance >= 0)
            {
                continue;
            }
            if (lotNo != System)
            {
                throw new DomainException(
                    LotRules.NegativeCode, itemCd, warehouseCode, locationCode, lotNo, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), balance);
            }
            throw new DomainException(NegativeCode, itemCd, warehouseCode, locationCode, lotNo, balance);
        }
    }

    /// <summary>Location management can be turned off only when no Location other than '*' has a balance (MM-BR-718).</summary>
    /// <exception cref="DomainException">MM-E-718.</exception>
    public static void EnsureCanStopLocationManagement(string itemCd, bool locationStockLeft)
    {
        if (locationStockLeft)
        {
            throw new DomainException(LocationManagementCode, itemCd);
        }
    }
}

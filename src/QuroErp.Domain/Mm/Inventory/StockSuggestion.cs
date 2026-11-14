namespace QuroErp.Domain.Mm.Inventory;

/// <summary>The balance of one (Location, Lot) of an item in a warehouse, with the Lot's received date (MM-BR-364).</summary>
public sealed record StockBalance(long LocationId, string LocationCode, long LotId, string LotNo, DateOnly? ReceivedDt, decimal Qty);

/// <summary>One part of a suggested issue: take Qty from this (Location, Lot).</summary>
public sealed record StockPick(long LocationId, string LocationCode, long LotId, string LotNo, decimal Qty);

/// <summary>FIFO suggestion of the Locations and Lots a decrease takes (MM-BR-716).</summary>
public static class StockSuggestion
{
    /// <summary>
    /// Balances above zero in FIFO order: Lot '*' first, then the Lot's received date, Lot number and Location code
    /// (MM-BR-716, MM-BR-362).
    /// </summary>
    public static IReadOnlyList<StockBalance> FifoOrder(IEnumerable<StockBalance> balances) =>
        [.. balances.Where(b => b.Qty > 0)
            .OrderBy(b => b.LotNo == StockKeyRules.System ? 0 : 1)
            .ThenBy(b => b.ReceivedDt ?? DateOnly.MinValue)
            .ThenBy(b => b.LotNo, StringComparer.Ordinal)
            .ThenBy(b => b.LocationCode, StringComparer.Ordinal)];

    /// <summary>
    /// Fills qty from the balances in FIFO order, one pick per (Location, Lot), splitting when one is not enough. When the
    /// balances are short, the picks hold what there is and the rest is left to the user.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">qty is not above zero.</exception>
    public static IReadOnlyList<StockPick> Fifo(IEnumerable<StockBalance> balances, decimal qty)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qty);
        var picks = new List<StockPick>();
        var left = qty;
        foreach (var balance in FifoOrder(balances))
        {
            if (left == 0)
            {
                break;
            }
            var take = Math.Min(left, balance.Qty);
            picks.Add(new StockPick(balance.LocationId, balance.LocationCode, balance.LotId, balance.LotNo, take));
            left -= take;
        }
        return picks;
    }

    /// <summary>The part of qty the picks do not cover: qty minus their sum, 0 when they cover it (MM-BR-716).</summary>
    public static decimal ShortQty(IEnumerable<StockPick> picks, decimal qty) => Math.Max(qty - picks.Sum(p => p.Qty), 0);
}

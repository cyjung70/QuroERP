using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>Pallet rules (MM pallet.md).</summary>
public static class PalletRules
{
    /// <summary>The Pallet qty of a receipt line is not above 0, or the line has no stock (MM-BR-910). Arg: line.</summary>
    public const string QtyCode = "MM-E-910";

    /// <summary>
    /// A Pallet cannot change this way (MM-BR-911): a receipt line whose Pallets were already moved, shipped, split, merged or
    /// adjusted cannot be deleted. Arg: Pallet number.
    /// </summary>
    public const string ChangedCode = "MM-E-911";

    /// <summary>What a Pallet line's shipment writes on the Pallet and what a line delete or Void writes back (MM-BR-913).</summary>
    public const string ShipEvent = "SHIP";

    /// <summary>
    /// A decrease that is not a Pallet line leaves the key with less than its ACTIVE Pallets hold. Args: item code, warehouse
    /// code, Location code, lot number, the key balance after the decrease, the Pallet quantity.
    /// </summary>
    public const string PalletStockCode = "MM-E-916";

    /// <summary>MM-BR-910: a Pallet qty is above 0 and only on a line of a stock item. Null makes no Pallet.</summary>
    /// <exception cref="DomainException">MM-E-910 with the line.</exception>
    public static void EnsurePalletQty(int seq, decimal? palletQty, bool stockItem)
    {
        if (palletQty is { } qty && (qty <= 0 || !stockItem))
        {
            throw new DomainException(QtyCode, seq);
        }
    }

    /// <summary>
    /// MM-BR-910: the line qty in Pallets of palletQty each, the last one with the rest (25 by 10 is 10, 10, 5).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">qty or palletQty not above 0.</exception>
    public static IReadOnlyList<decimal> Split(decimal qty, decimal palletQty)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(palletQty);
        var full = (long)decimal.Floor(qty / palletQty);
        var rest = qty - (full * palletQty);
        var pallets = new List<decimal>();
        for (var i = 0L; i < full; i++)
        {
            pallets.Add(palletQty);
        }
        if (rest > 0)
        {
            pallets.Add(rest);
        }
        return pallets;
    }

    /// <summary>
    /// MM-BR-911: deleting a receipt line closes its Pallets, which is only right while each is as the receipt made it: ACTIVE,
    /// with its first qty and Location and no event but its creation.
    /// </summary>
    /// <exception cref="DomainException">MM-E-911 with the Pallet number.</exception>
    public static void EnsureAsReceived(string palletNo, bool asReceived)
    {
        if (!asReceived)
        {
            throw new DomainException(ChangedCode, palletNo);
        }
    }

    /// <summary>
    /// MM-BR-916: the ACTIVE Pallets of a (item, warehouse, Location, Lot) never hold more than its balance, so a decrease that is
    /// not a Pallet line may only take the stock outside the Pallets.
    /// </summary>
    /// <exception cref="DomainException">MM-E-916 when balanceAfter is below palletQty.</exception>
    public static void EnsurePalletsCovered(
        string itemCd, string warehouseCode, string locationCode, string lotNo, decimal balanceAfter, decimal palletQty)
    {
        if (balanceAfter < palletQty)
        {
            throw new DomainException(PalletStockCode, itemCd, warehouseCode, locationCode, lotNo, balanceAfter, palletQty);
        }
    }
}

using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>Pallet rules the stock service checks (MM pallet.md).</summary>
public static class PalletRules
{
    /// <summary>
    /// A decrease that is not a Pallet line leaves the key with less than its ACTIVE Pallets hold. Args: item code, warehouse
    /// code, Location code, lot number, the key balance after the decrease, the Pallet quantity.
    /// </summary>
    public const string PalletStockCode = "MM-E-916";

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

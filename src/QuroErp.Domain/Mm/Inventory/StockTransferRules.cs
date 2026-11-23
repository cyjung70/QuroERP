using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>Rules of a stock transfer document (MM warehouse.md MM-BR-705~708, MM-BR-715; lot.md MM-BR-357; pallet.md MM-BR-912).</summary>
public static class StockTransferRules
{
    /// <summary>
    /// The transfer type is not TR or LT, a TR stays in one warehouse, or an LT leaves its warehouse or keeps the Location.
    /// Args: movement type, line number.
    /// </summary>
    public const string InvalidCode = "MM-E-715";

    /// <summary>
    /// A TR line whose sending and receiving warehouses are the same, whose qty is not above 0, or whose item holds no stock of its
    /// own (a service or a Kit). Args: line number, item code.
    /// </summary>
    public const string LineCode = "MM-E-705";

    /// <summary>
    /// A Pallet line that does not move the Pallet whole: the Pallet is not ACTIVE where the line takes it from, holds another item
    /// or Lot, has another qty, or is on two lines. Args: line number, Pallet number (its id when it does not exist).
    /// </summary>
    public const string PalletCode = "MM-E-912";

    /// <returns>True for TR and LT, the two types of a transfer document (MM-BR-715).</returns>
    public static bool IsType(string? movType) => movType is MovementType.StockTransfer or MovementType.LocationTransfer;

    /// <summary>
    /// MM-BR-705: a TR goes between two warehouses, every qty is above 0 and every item holds stock of its own (MM-BR-008). A Kit
    /// has no stock of its own: its components move (MM-BR-007).
    /// </summary>
    /// <exception cref="DomainException">MM-E-705 with the line number and the item code.</exception>
    public static void EnsureLineValues(
        string movType, int lineNo, string itemCd, decimal qty, bool holdsStock, long fromWarehouseId, long toWarehouseId)
    {
        if ((movType == MovementType.StockTransfer && fromWarehouseId == toWarehouseId) || qty <= 0 || !holdsStock)
        {
            throw new DomainException(LineCode, lineNo, itemCd);
        }
    }

    /// <summary>
    /// TR moves between two warehouses; LT stays in one warehouse and moves between two Locations. Both keep the Lot
    /// (MM-BR-357).
    /// </summary>
    /// <exception cref="DomainException">MM-E-715.</exception>
    public static void EnsureLine(
        string? movType, int lineNo, long fromWarehouseId, long toWarehouseId, long fromLocationId, long toLocationId)
    {
        var valid = movType switch
        {
            MovementType.StockTransfer => fromWarehouseId != toWarehouseId,
            MovementType.LocationTransfer => fromWarehouseId == toWarehouseId && fromLocationId != toLocationId,
            _ => false,
        };
        if (!valid)
        {
            throw new DomainException(InvalidCode, movType ?? "", lineNo);
        }
    }

    /// <summary>
    /// MM-BR-706: the amount of both movements of a line, the moving average cost at the transfer × qty rounded half up to the
    /// base currency's decimals. The issue takes it out and the receipt puts the same amount back, so the item's stock value
    /// and cost do not change.
    /// </summary>
    public static decimal Amount(decimal qty, decimal price, int baseCurrencyDecimals) =>
        Rounding.Round(qty * price, baseCurrencyDecimals, RoundingMode.HalfUp);

    /// <summary>
    /// MM-BR-912: a Pallet line moves the whole Pallet. The Pallet must be ACTIVE at the warehouse and Location the line takes it
    /// from, hold the line's item and qty and, when the line names a Lot, that Lot. Undoing a transfer checks the Pallet the same
    /// way at the receiving side.
    /// </summary>
    /// <param name="pallet">The locked Pallet, null when no Pallet has the line's id.</param>
    /// <param name="locationId">The Location the line takes it from, null when the line does not name one (the Pallet's is used).</param>
    /// <param name="lotId">The Lot the line names, null when it names none (the Pallet's is used).</param>
    /// <exception cref="DomainException">MM-E-912 with the line number and the Pallet number (its id when it does not exist).</exception>
    public static void EnsurePallet(
        int lineNo, long palletId, PalletState? pallet, long itemId, decimal qty, long warehouseId, long? locationId, long? lotId)
    {
        if (pallet is null)
        {
            throw new DomainException(PalletCode, lineNo, palletId);
        }
        if (pallet.Status != PalletRules.Active || pallet.ItemId != itemId || pallet.Qty != qty || pallet.WarehouseId != warehouseId
            || (locationId is { } location && pallet.LocationId != location) || (lotId is { } lot && pallet.LotId != lot))
        {
            throw new DomainException(PalletCode, lineNo, pallet.PalletNo);
        }
    }

    /// <summary>MM-BR-912: one Pallet goes on one line only.</summary>
    /// <exception cref="DomainException">MM-E-912 with the second line and the Pallet id.</exception>
    public static void EnsurePalletsOnce(IEnumerable<(int LineNo, long? PalletId)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var seen = new HashSet<long>();
        foreach (var (lineNo, palletId) in lines)
        {
            if (palletId is { } id && !seen.Add(id))
            {
                throw new DomainException(PalletCode, lineNo, id);
            }
        }
    }
}

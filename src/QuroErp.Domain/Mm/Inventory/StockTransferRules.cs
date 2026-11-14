using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>Movement type rules of a stock transfer document (MM-BR-715).</summary>
public static class StockTransferRules
{
    /// <summary>
    /// The transfer type is not TR or LT, a TR stays in one warehouse, or an LT leaves its warehouse or keeps the Location.
    /// Args: movement type, line number.
    /// </summary>
    public const string InvalidCode = "MM-E-715";

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
}

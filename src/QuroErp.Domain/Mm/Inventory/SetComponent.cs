using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>One component of a set item: componentQty units of ComponentItemId per set.</summary>
public sealed record SetComponent(long SetItemId, long ComponentItemId, decimal ComponentQty)
{
    /// <summary>A set with no components cannot move stock. Args: set item code.</summary>
    public const string NoComponents = "SD-E-509";

    /// <summary>
    /// A set keeps no stock. A set movement of qty becomes one movement per component of qty × component qty,
    /// keeping the sign and without rounding (MM-BR-007).
    /// </summary>
    /// <exception cref="DomainException">SD-E-509 when the set has no components (SD-BR-509).</exception>
    public static IReadOnlyList<(long ItemId, decimal Qty)> Expand(string setItemCd, decimal qty, IEnumerable<SetComponent> components)
    {
        var expanded = components.Select(c => (c.ComponentItemId, qty * c.ComponentQty)).ToList();
        if (expanded.Count == 0)
        {
            throw new DomainException(NoComponents, setItemCd);
        }
        return expanded;
    }

    /// <summary>
    /// A Kit component's stock does not cover the decrease. Args: Kit item code, component item code, warehouse code, needed
    /// qty, available qty.
    /// </summary>
    public const string NotEnoughStock = "SD-E-518";

    /// <summary>
    /// The Locations and Lots a Kit component decrease of qty takes: the balances in FIFO order (MM-BR-716), split when one is
    /// not enough. The user does not pick them (SD-BR-518, MM-BR-007).
    /// </summary>
    /// <exception cref="DomainException">SD-E-518 when the balances add up to less than qty.</exception>
    public static IReadOnlyList<StockPick> AllocateFifo(
        string setItemCd, string componentCd, string warehouseCode, IEnumerable<StockBalance> balances, decimal qty)
    {
        var picks = StockSuggestion.Fifo(balances, qty);
        var shortQty = StockSuggestion.ShortQty(picks, qty);
        if (shortQty > 0)
        {
            throw new DomainException(NotEnoughStock, setItemCd, componentCd, warehouseCode, qty, qty - shortQty);
        }
        return picks;
    }
}

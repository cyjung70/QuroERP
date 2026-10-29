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
}

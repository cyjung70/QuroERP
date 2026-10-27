namespace QuroErp.Domain.Mm.Inventory;

/// <summary>One component of a set item: componentQty units of ComponentItemId per set.</summary>
public sealed record SetComponent(long SetItemId, long ComponentItemId, decimal ComponentQty)
{
    /// <summary>
    /// A set keeps no stock. A set movement of qty becomes one movement per component of qty × component qty,
    /// keeping the sign (MM-BR-007).
    /// </summary>
    public static IReadOnlyList<(long ItemId, decimal Qty)> Expand(decimal qty, IEnumerable<SetComponent> components) =>
        components.Select(c => (c.ComponentItemId, qty * c.ComponentQty)).ToList();
}

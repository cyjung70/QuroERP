namespace QuroErp.Domain.Mm.Inventory;

/// <summary>
/// Item-level stock in bc_item: quantity (sum of all warehouses), stock value and moving average cost.
/// Value and cost are per item, not per warehouse (inventory.md).
/// </summary>
public sealed class ItemStock(
    long itemId, string itemCd, string? type, decimal onHandQty, decimal stockValue, decimal movingAverageCost,
    bool lotManaged = false, DateOnly? lotManagedFrom = null, bool locationManaged = false)
{
    /// <summary>ITEM_TYPE codes.</summary>
    public const string Inventory = "INVENTORY";

    /// <summary>A sales BOM: no stock of its own, movements expand into its components (SD-BR-511, MM-BR-007).</summary>
    public const string Kit = "KIT";

    /// <summary>An assembled item with stock of its own (SD-BR-511, SD-BR-513).</summary>
    public const string Set = "SET";
    public const string Service = "SERVICE";

    public long ItemId { get; } = itemId;

    public string ItemCd { get; } = itemCd;

    public string? Type { get; } = type;

    /// <summary>bc_item.onhand_qty: all warehouses together.</summary>
    public decimal OnHandQty { get; private set; } = onHandQty;

    /// <summary>bc_item.stk_val.</summary>
    public decimal StockValue { get; private set; } = stockValue;

    /// <summary>bc_item.mvg_cost.</summary>
    public decimal MovingAverageCost { get; private set; } = movingAverageCost;

    /// <summary>
    /// Every item type except Service keeps stock: a Kit through its components (MM-BR-007), a Set on its own (MM-BR-008,
    /// SD-BR-513).
    /// </summary>
    public bool IsStockManaged => Type != Service;

    /// <summary>Movements of a Kit expand into its components (MM-BR-007).</summary>
    public bool IsKit => Type == Kit;

    /// <summary>bc_item.lot_managed: stock may use lots other than '*' (MM-BR-351).</summary>
    public bool LotManaged { get; } = lotManaged;

    /// <summary>bc_item.lot_managed_from: movements dated before it are blocked (MM-BR-356).</summary>
    public DateOnly? LotManagedFrom { get; } = lotManagedFrom;

    /// <summary>bc_item.location_managed: stock may use Locations other than '*' (MM-BR-713).</summary>
    public bool LocationManaged { get; } = locationManaged;

    /// <summary>
    /// Applies one movement. A receipt or receipt reversal recalculates the moving average cost (MM-BR-005):
    /// stock above zero → value / qty; stock at or below zero → keep the cost; stock going from ≤ 0 to &gt; 0 →
    /// cost = this price and value = qty × cost.
    /// </summary>
    public void Apply(decimal qty, decimal amount, decimal price, bool recalculateCost)
    {
        var before = OnHandQty;
        OnHandQty += qty;
        StockValue += amount;
        if (!recalculateCost || OnHandQty <= 0)
        {
            return;
        }
        if (before <= 0)
        {
            MovingAverageCost = price;
            StockValue = OnHandQty * price;
        }
        else
        {
            MovingAverageCost = StockValue / OnHandQty;
        }
    }
}

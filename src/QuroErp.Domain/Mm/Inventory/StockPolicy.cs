using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.Inventory;

/// <summary>
/// Negative stock check against the item + warehouse on-hand quantity (inventory.md "마이너스 재고 허용 옵션").
/// Infrastructure reads the setting and the locked on-hand row; the decision is made here.
/// Point-in-time and month-end checks (MM-BR-009, MM-BR-010) are TASK-CORE-009.
/// </summary>
public static class StockPolicy
{
    /// <summary>
    /// Not enough stock in the warehouse. Args: item code, warehouse code, on hand, requested qty, set item code
    /// (empty unless the item is a component of a set line).
    /// </summary>
    public const string InsufficientStock = "MM-E-001";

    /// <summary>The mode when mm_inventory_setting has no row (MM-BR-003).</summary>
    public static NegativeStockMode ModeOrDefault(string? code) =>
        NegativeStockModeCode.TryParse(code, out var mode) ? mode : NegativeStockMode.Deny;

    /// <summary>
    /// Checks one movement of qty against onHand. Only movements that decrease stock are checked (MM-BR-602).
    /// </summary>
    /// <returns>true when the movement leaves the stock below zero and is allowed (flag it, MM-BR-002).</returns>
    /// <exception cref="DomainException">MM-E-001 in DENY mode when the stock would go below zero (MM-BR-001).</exception>
    public static bool CheckOnHand(NegativeStockMode mode, decimal onHand, decimal qty, string itemCd, string warehouseCode, string setItemCd = "")
    {
        if (qty >= 0 || onHand + qty >= 0)
        {
            return false;
        }
        if (mode == NegativeStockMode.Deny)
        {
            throw new DomainException(InsufficientStock, itemCd, warehouseCode, onHand, -qty, setItemCd);
        }
        return true;
    }
}

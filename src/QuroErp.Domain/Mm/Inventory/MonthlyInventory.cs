namespace QuroErp.Domain.Mm.Inventory;

/// <summary>
/// One month of stock: opening, receipts, issues and closing (MM-BR-006). The caller picks the level:
/// quantities per item + warehouse, amounts per item over all warehouses.
/// </summary>
public sealed record MonthlyInventory(decimal BasQty, decimal BasAmt, decimal RcptQty, decimal RcptAmt, decimal IssueQty, decimal IssueAmt)
{
    public decimal BalQty => BasQty + RcptQty - IssueQty;

    public decimal BalAmt => BasAmt + RcptAmt - IssueAmt;

    /// <summary>
    /// The month after a closing balance. A month without movements carries the balance forward.
    /// Receipt types (GR, RT, BP, UR) count as receipts and their reversals reduce receipts; issue types (GI, DN)
    /// count as issues; CT and TR count by sign.
    /// </summary>
    public static MonthlyInventory Roll(decimal openingQty, decimal openingAmt, IEnumerable<(string MovType, decimal Qty, decimal Amount)> movements)
    {
        decimal rcptQty = 0, rcptAmt = 0, issueQty = 0, issueAmt = 0;
        foreach (var (movType, qty, amount) in movements)
        {
            var isReceipt = MovementType.OriginalSign(movType) switch
            {
                1 => true,
                -1 => false,
                _ => qty > 0,
            };
            if (isReceipt)
            {
                rcptQty += qty;
                rcptAmt += amount;
            }
            else
            {
                issueQty -= qty;
                issueAmt -= amount;
            }
        }
        return new MonthlyInventory(openingQty, openingAmt, rcptQty, rcptAmt, issueQty, issueAmt);
    }
}

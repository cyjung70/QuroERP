namespace QuroErp.Domain.Mm.Inventory;

/// <summary>
/// mm_goods_movement.mov_type codes (code MOVEMENT_TYPE, inventory.md). The type fixes the sign of an original
/// movement's qty (MM-BR-606).
/// </summary>
public static class MovementType
{
    public const string GoodsReceipt = "GR";
    public const string Return = "RT";
    public const string SetAssembly = "BP";
    public const string UnplannedReceipt = "UR";
    public const string StockCount = "CT";
    public const string GoodsIssue = "GI";
    public const string Delivery = "DN";
    public const string StockTransfer = "TR";

    public static bool IsKnown(string? type) => type is GoodsReceipt or Return or SetAssembly or UnplannedReceipt
        or StockCount or GoodsIssue or Delivery or StockTransfer;

    /// <summary>
    /// Receipts and their reversals recalculate the moving average cost (MM-BR-005). A stock count increase (CT, qty &gt; 0)
    /// is treated as a receipt.
    /// </summary>
    public static bool RecalculatesCost(string type, decimal qty) =>
        type is GoodsReceipt or Return or SetAssembly or UnplannedReceipt || (type == StockCount && qty > 0);

    /// <returns>+1 or −1 for the sign an original movement must have, 0 when either sign is allowed (CT, TR).</returns>
    public static int OriginalSign(string type) => type switch
    {
        GoodsReceipt or Return or SetAssembly or UnplannedReceipt => 1,
        GoodsIssue or Delivery => -1,
        StockCount or StockTransfer => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}

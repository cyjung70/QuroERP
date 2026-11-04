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

    /// <summary>Supplier invoice price difference: qty 0, only the stock value changes (MM-BR-759).</summary>
    public const string PriceVariance = "PV";

    /// <summary>Vendor return: an issue at the original receipt price (MM-BR-785).</summary>
    public const string VendorReturn = "VR";

    public static bool IsKnown(string? type) => type is GoodsReceipt or Return or SetAssembly or UnplannedReceipt
        or StockCount or GoodsIssue or Delivery or StockTransfer or PriceVariance or VendorReturn;

    /// <summary>PV has qty 0 and an amount that is not 0 (MM-BR-606).</summary>
    public static bool IsAmountOnly(string type) => type == PriceVariance;

    /// <summary>
    /// Receipts and their reversals recalculate the moving average cost (MM-BR-005). A stock count increase (CT, qty &gt; 0)
    /// is treated as a receipt. A price difference (PV) changes the value and a vendor return (VR) leaves at the receipt
    /// price, so both recalculate it as well.
    /// </summary>
    public static bool RecalculatesCost(string type, decimal qty) =>
        type is GoodsReceipt or Return or SetAssembly or UnplannedReceipt or PriceVariance or VendorReturn
        || (type == StockCount && qty > 0);

    /// <returns>
    /// +1 or −1 for the sign an original movement's qty must have, 0 when either sign is allowed (CT, TR) or the qty is 0 (PV).
    /// </returns>
    public static int OriginalSign(string type) => type switch
    {
        GoodsReceipt or Return or SetAssembly or UnplannedReceipt => 1,
        GoodsIssue or Delivery or VendorReturn => -1,
        StockCount or StockTransfer or PriceVariance => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}

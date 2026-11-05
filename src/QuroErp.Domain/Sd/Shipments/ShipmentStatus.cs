namespace QuroErp.Domain.Sd.Shipments;

/// <summary>
/// Shipment status (SD-BR-166): OP not billed, PB partly billed, BL billed, VO void. A BILL_THEN_SHIP shipment was billed before
/// it shipped, so its lines are billed when saved.
/// </summary>
public static class ShipmentStatus
{
    public const string Open = "OP";
    public const string PartlyBilled = "PB";
    public const string Billed = "BL";
    public const string Void = "VO";

    /// <summary>SD-BR-166: billed qty 0 → OP, every line billed in full → BL, otherwise PB. A shipment without lines is OP.</summary>
    public static string FromBilling(IEnumerable<(decimal ShipQty, decimal BilledQty)> lines)
    {
        var list = lines.ToList();
        if (list.Sum(l => l.BilledQty) == 0)
        {
            return Open;
        }
        return list.All(l => l.BilledQty == l.ShipQty) ? Billed : PartlyBilled;
    }
}

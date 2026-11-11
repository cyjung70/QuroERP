using QuroErp.Domain.Common;

namespace QuroErp.Domain.Sd.SalesOrders;

/// <summary>
/// Sales order status (S500): SO entered, RE shipping in progress, CL completed or closed (SD-BR-012). Shipments set it;
/// the order screen never does.
/// </summary>
public static class SalesOrderStatus
{
    public const string Entered = "SO";
    public const string Released = "RE";
    public const string Closed = "CL";

    /// <summary>The order is not SO, so it cannot be changed or deleted (SD-BR-003). Arg: so_no.</summary>
    public const string NotEditableCode = "SD-E-003";

    /// <summary>SD-BR-012: no quantity shipped → SO, nothing left to ship → CL, otherwise RE. Invoices do not count.</summary>
    public static string FromShipments(IEnumerable<(decimal SoQty, decimal ShipQty)> lines)
    {
        var list = lines.ToList();
        if (list.Sum(l => l.ShipQty) == 0)
        {
            return Entered;
        }
        return list.Sum(l => l.SoQty - l.ShipQty) <= 0 ? Closed : Released;
    }

    /// <exception cref="DomainException">SD-E-003 unless the status is SO (SD-BR-003).</exception>
    public static void EnsureEditable(string status, string soNo)
    {
        if (status != Entered)
        {
            throw new DomainException(NotEditableCode, soNo);
        }
    }
}

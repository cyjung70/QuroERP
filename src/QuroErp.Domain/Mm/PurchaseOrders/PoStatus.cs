namespace QuroErp.Domain.Mm.PurchaseOrders;

/// <summary>
/// Purchase order status (code PO_STATUS, MM-BR-019). Stored as the string code in mm_purchase_order.status.
/// Migrated legacy orders keep PO, RE and CL; a migrated order is PO.
/// </summary>
public enum PoStatus
{
    /// <summary>DR: saved, not approved yet. Editable and deletable, never cancelled.</summary>
    Draft,

    /// <summary>PO: approved. Editable until the first receipt (MM-BR-021).</summary>
    Approved,

    /// <summary>RE: some quantity received.</summary>
    PartiallyReceived,

    /// <summary>CL: all quantity received.</summary>
    Received,

    /// <summary>MC: closed manually, no further receipts (MM-BR-022).</summary>
    Closed,

    /// <summary>CN: cancelled before any receipt (MM-BR-023).</summary>
    Cancelled,
}

/// <summary>String codes of PoStatus stored in mm_purchase_order.status.</summary>
public static class PoStatusCode
{
    public const string Draft = "DR";
    public const string Approved = "PO";
    public const string PartiallyReceived = "RE";
    public const string Received = "CL";
    public const string Closed = "MC";
    public const string Cancelled = "CN";

    public static string ToCode(this PoStatus status) => status switch
    {
        PoStatus.Draft => Draft,
        PoStatus.Approved => Approved,
        PoStatus.PartiallyReceived => PartiallyReceived,
        PoStatus.Received => Received,
        PoStatus.Closed => Closed,
        PoStatus.Cancelled => Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <returns>false when code is not one of the six codes.</returns>
    public static bool TryParse(string? code, out PoStatus status)
    {
        (var ok, status) = code switch
        {
            Draft => (true, PoStatus.Draft),
            Approved => (true, PoStatus.Approved),
            PartiallyReceived => (true, PoStatus.PartiallyReceived),
            Received => (true, PoStatus.Received),
            Closed => (true, PoStatus.Closed),
            Cancelled => (true, PoStatus.Cancelled),
            _ => (false, PoStatus.Draft),
        };
        return ok;
    }
}

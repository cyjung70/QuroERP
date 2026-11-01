namespace QuroERP.Web.Features.MM.PurchaseOrder;

/// <summary>
/// Which commands are enabled for a given PO status.
/// Rule from the business owner: once any quantity is received, the PO can no longer be edited or deleted.
/// The API enforces the same rules; the UI only disables buttons.
/// </summary>
public static class PoStatusRules
{
    public static bool HasReceipt(PoDocument d) => d.Lines.Any(l => l.ReceivedQty > 0);

    public static bool IsEditable(PoDocument d) =>
        (d.Header.Status is PoStatus.Draft or PoStatus.Approved) && !HasReceipt(d);

    public static bool CanSave(PoDocument d) => IsEditable(d);
    public static bool CanEditRows(PoDocument d) => IsEditable(d);
    public static bool CanDelete(PoDocument d) => d.Header.PoNo is not null && IsEditable(d);
    public static bool CanApprove(PoDocument d) => d.Header.PoNo is not null && d.Header.Status == PoStatus.Draft;
    public static bool CanClose(PoDocument d) => d.Header.Status is PoStatus.Approved or PoStatus.PartiallyReceived;
    public static bool CanCancel(PoDocument d) => d.Header.Status == PoStatus.Approved && !HasReceipt(d);
    public static bool CanPrint(PoDocument d) => d.Header.PoNo is not null;

    /// Lines with ReceivedQty > 0 are locked even while the header is editable (defensive; normally unreachable).
    public static bool IsLineEditable(PoDocument d, PoLine l) => IsEditable(d) && l.ReceivedQty == 0;
}
